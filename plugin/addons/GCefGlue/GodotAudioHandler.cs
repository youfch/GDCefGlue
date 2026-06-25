using System;
using System.Runtime.InteropServices;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlue
{
    // 当前音频驱动使用 Windows winmm waveOut API 直接透传 CEF PCM 数据。
    //
    // 已知限制：
    //   - 同步阻塞播放（OnAudioStreamPacket 中轮询 WHDR_DONE），高负载下可能产生音频卡顿
    //   - 仅 Windows（依赖 winmm.dll）
    //   - CEF 默认构建不含专有编解码器（AAC/H.264/MP3），音频按钮会处于禁用状态
    //   - 后续可考虑改用 WASAPI 或路由到 Godot AudioServer
    //
    // 替代方案参考：
    //   - WASAPI 共享模式（更低延迟，需 ComImport interop）
    //   - Godot AudioStreamGenerator（可在引擎内混音，但需手动管理缓冲队列）

    /// <summary>
    /// Audio handler that captures PCM audio from CEF and plays it through
    /// the Windows waveOut API. Required because in windowless rendering mode
    /// CEF does not output audio directly to the OS; it delivers PCM packets
    /// via this handler instead.
    /// </summary>
    internal sealed class GodotAudioHandler : CefAudioHandler
    {
        private const uint WAVE_MAPPER = 0xFFFFFFFF;
        private const ushort WAVE_FORMAT_IEEE_FLOAT = 0x0003;
        private const uint CALLBACK_NULL = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint waveOutOpen(out IntPtr hWaveOut, uint uDeviceID,
            ref WAVEFORMATEX pwfx, IntPtr dwCallback, IntPtr dwCallbackInstance, uint dwFlags);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint waveOutClose(IntPtr hWaveOut);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint waveOutPrepareHeader(IntPtr hWaveOut, ref WAVEHDR pwh, uint cbwh);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint waveOutUnprepareHeader(IntPtr hWaveOut, ref WAVEHDR pwh, uint cbwh);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint waveOutWrite(IntPtr hWaveOut, ref WAVEHDR pwh, uint cbwh);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint waveOutReset(IntPtr hWaveOut);

        private IntPtr _hWaveOut = IntPtr.Zero;
        private int _channels;
        private int _sampleRate;
        private int _framesPerBuffer;
        private int _bytesPerFrame;
        private int _bufferSize;

        private readonly object _lock = new object();
        private bool _disposed;

        protected override bool GetAudioParameters(CefBrowser browser, CefAudioParameters parameters)
        {
            return true;
        }

        protected override void OnAudioStreamStarted(CefBrowser browser, in CefAudioParameters parameters, int channels)
        {
            lock (_lock)
            {
                CloseWaveOut();

                _channels = channels;
                _sampleRate = parameters.SampleRate > 0 ? parameters.SampleRate : 44100;
                _framesPerBuffer = parameters.FramesPerBuffer > 0 ? parameters.FramesPerBuffer : 1024;
                _bytesPerFrame = _channels * sizeof(float);
                _bufferSize = _framesPerBuffer * _bytesPerFrame;

                var format = new WAVEFORMATEX
                {
                    wFormatTag = WAVE_FORMAT_IEEE_FLOAT,
                    nChannels = (ushort)_channels,
                    nSamplesPerSec = (uint)_sampleRate,
                    wBitsPerSample = sizeof(float) * 8,
                    nBlockAlign = (ushort)_bytesPerFrame,
                    nAvgBytesPerSec = (uint)(_sampleRate * _bytesPerFrame),
                    cbSize = 0
                };

                var result = waveOutOpen(out _hWaveOut, WAVE_MAPPER, ref format, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL);
                if (result != 0)
                {
                    GD.PrintErr($"GodotAudioHandler: waveOutOpen failed (MMSYSERR_{result})");
                    _hWaveOut = IntPtr.Zero;
                }
                else
                {
                    GD.Print($"GodotAudioHandler: Audio stream started - {channels}ch, {_sampleRate}Hz, {_framesPerBuffer} frames/buffer");
                }
            }
        }

        protected override void OnAudioStreamPacket(CefBrowser browser, IntPtr data, int frames, long pts)
        {
            if (_hWaveOut == IntPtr.Zero || data == IntPtr.Zero || frames <= 0)
                return;

            int byteCount = frames * _bytesPerFrame;
            byte[] buffer = new byte[byteCount];
            Marshal.Copy(data, buffer, 0, byteCount);

            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

            var hdr = new WAVEHDR
            {
                lpData = handle.AddrOfPinnedObject(),
                dwBufferLength = (uint)byteCount,
                dwFlags = 0,
                dwLoops = 1
            };

            try
            {
                waveOutPrepareHeader(_hWaveOut, ref hdr, (uint)Marshal.SizeOf<WAVEHDR>());
                waveOutWrite(_hWaveOut, ref hdr, (uint)Marshal.SizeOf<WAVEHDR>());

                // 同步等待播放完成 — 简单但可能产生延迟
                while ((hdr.dwFlags & 0x1) == 0)
                {
                    System.Threading.Thread.Sleep(1);
                }

                waveOutUnprepareHeader(_hWaveOut, ref hdr, (uint)Marshal.SizeOf<WAVEHDR>());
            }
            finally
            {
                handle.Free();
            }
        }

        protected override void OnAudioStreamStopped(CefBrowser browser)
        {
            lock (_lock)
            {
                CloseWaveOut();
                GD.Print("GodotAudioHandler: Audio stream stopped");
            }
        }

        protected override void OnAudioStreamError(CefBrowser browser, string message)
        {
            GD.PrintErr($"GodotAudioHandler: Audio stream error - {message}");
        }

        private void CloseWaveOut()
        {
            if (_hWaveOut != IntPtr.Zero)
            {
                waveOutReset(_hWaveOut);
                waveOutClose(_hWaveOut);
                _hWaveOut = IntPtr.Zero;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                CloseWaveOut();
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
