using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MareyaUSBFix.Core
{
    /// <summary>Prévient quand une clé USB ou un disque est branché ou retiré.</summary>
    public sealed class DeviceWatcher : NativeWindow, IDisposable
    {
        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        private const int DBT_DEVTYP_VOLUME = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct DEV_BROADCAST_HDR
        {
            public int dbch_size;
            public int dbch_devicetype;
            public int dbch_reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEV_BROADCAST_VOLUME
        {
            public int dbcv_size;
            public int dbcv_devicetype;
            public int dbcv_reserved;
            public int dbcv_unitmask;
            public short dbcv_flags;
        }

        /// <summary>Racine du lecteur branché, par exemple « E:\ ». Déclenché sur le fil de l'interface.</summary>
        public event Action<string> DriveArrived;
        public event Action DrivesChanged;

        private readonly Control _syncTarget;

        public DeviceWatcher(Control syncTarget)
        {
            _syncTarget = syncTarget;
            // fenêtre de premier niveau invisible : elle reçoit les messages diffusés par Windows
            CreateHandle(new CreateParams { Caption = "MareyaUSBFix.DeviceWatcher" });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE && m.LParam != IntPtr.Zero)
            {
                int evt = m.WParam.ToInt32();
                var hdr = Marshal.PtrToStructure<DEV_BROADCAST_HDR>(m.LParam);
                if (hdr.dbch_devicetype == DBT_DEVTYP_VOLUME && (evt == DBT_DEVICEARRIVAL || evt == DBT_DEVICEREMOVECOMPLETE))
                {
                    var vol = Marshal.PtrToStructure<DEV_BROADCAST_VOLUME>(m.LParam);
                    DrivesChanged?.Invoke();
                    if (evt == DBT_DEVICEARRIVAL)
                    {
                        for (int i = 0; i < 26; i++)
                        {
                            if ((vol.dbcv_unitmask & (1 << i)) == 0) continue;
                            string root = $"{(char)('A' + i)}:\\";
                            WaitReadyThenRaise(root);
                        }
                    }
                }
            }
            base.WndProc(ref m);
        }

        private void WaitReadyThenRaise(string root)
        {
            Task.Run(async () =>
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    try { if (new DriveInfo(root).IsReady) break; } catch { /* pas encore prêt */ }
                    await Task.Delay(500).ConfigureAwait(false);
                }
                if (_syncTarget.IsHandleCreated)
                    _syncTarget.BeginInvoke(new Action(() => DriveArrived?.Invoke(root)));
            });
        }

        public void Dispose() => DestroyHandle();
    }
}
