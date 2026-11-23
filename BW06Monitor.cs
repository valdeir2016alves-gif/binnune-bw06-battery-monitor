using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace BW06BatteryMonitor
{
    #region Native Win32, CoreAudio & CfgMgr32 Interop

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
        public PROPERTYKEY(Guid g, uint id) { fmtid = g; pid = id; }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPVARIANT
    {
        public ushort vt;
        public ushort r1;
        public ushort r2;
        public ushort r3;
        public IntPtr p1;
        public IntPtr p2;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IMMDeviceCollection ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint pcDevices);
        [PreserveSig] int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out int pdwState);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cProps);
        [PreserveSig] int GetAt(uint iProp, out PROPERTYKEY pkey);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float pfPeak);
        [PreserveSig] int GetMeteringChannelCount(out uint pnChannelCount);
        [PreserveSig] int GetChannelsPeakValues(uint u32ChannelCount, [Out] float[] afPeakValues);
        [PreserveSig] int QueryHardwareSupport(out uint pdwHardwareSupportMask);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int GetChannelCount(out uint pnChannelCount);
        [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }

    internal static class NativeMethods
    {
        public const uint DN_STARTED = 0x00000008;
        public const uint CM_LOCATE_DEVNODE_NORMAL = 0x00000000;

        [DllImport("ole32.dll")]
        public static extern int PropVariantClear(ref PROPVARIANT pvar);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern int CM_Get_Device_ID_List_SizeW(out uint pulLen, string pszFilter, uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern int CM_Get_Device_ID_ListW(string pszFilter, char[] Buffer, uint BufferLen, uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

        [DllImport("cfgmgr32.dll")]
        public static extern int CM_Get_DevNode_Status(out uint pulStatus, out uint pulProblemNumber, uint dnDevInst, uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern int CM_Get_DevNode_PropertyW(
            uint dnDevInst,
            ref PROPERTYKEY PropertyKey,
            out uint PropertyType,
            byte[] PropertyBuffer,
            ref uint PropertyBufferSize,
            uint ulFlags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern SafeFileHandle CreateFile(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIoEx(SafeFileHandle hFile, IntPtr lpOverlapped);

        [DllImport("kernel32.dll")]
        public static extern bool AttachConsole(int dwProcessId);
    }

    #endregion

    #region Hardware Probe & Telemetry Service

    public class HardwareSnapshot
    {
        public bool DongleConnected;
        public bool AudioEndpointFound;
        public float SpeakerPeak;
        public float SpeakerVolume;
        public bool SpeakerMuted;
        public float MicPeak;
        public bool BluetoothConnected;
        public int BluetoothBattery; // -1 if unavailable
        public string ActiveDeviceName;
    }

    public static class BW06HardwareProbe
    {
        private static readonly PROPERTYKEY PKEY_FriendlyName =
            new PROPERTYKEY(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

        private static readonly PROPERTYKEY DEVPKEY_NAME =
            new PROPERTYKEY(new Guid("b725f130-47ef-101a-a5f1-02608c9eebac"), 10);

        private static readonly PROPERTYKEY DEVPKEY_Bluetooth_Battery =
            new PROPERTYKEY(new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5"), 2);

        private static readonly PROPERTYKEY DEVPKEY_Bluetooth_IsConnected =
            new PROPERTYKEY(new Guid("83da6326-97a6-4088-9453-a1923f573b29"), 15);

        private static Guid IID_IAudioMeterInformation = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");
        private static Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");

        public static HardwareSnapshot Poll(bool checkBluetooth)
        {
            var snap = new HardwareSnapshot();
            snap.BluetoothBattery = -1;
            snap.SpeakerVolume = 0.70f;
            snap.ActiveDeviceName = "BW06-BT";

            // 1. Check 2.4GHz USB Dongle (VID_10D6 & PID_4801)
            snap.DongleConnected = IsDevicePrefixStarted("USB\\VID_10D6&PID_4801");

            // 2. Check CoreAudio peak & volume on BW06 endpoints
            PollCoreAudio(snap);

            // 3. Check Bluetooth battery if requested
            if (checkBluetooth)
            {
                PollBluetoothBattery(snap);
            }

            return snap;
        }

        private static bool IsDevicePrefixStarted(string filterPrefix)
        {
            uint len;
            // CM_GETIDLIST_FILTER_PRESENT = 0x00000100
            if (NativeMethods.CM_Get_Device_ID_List_SizeW(out len, null, 0x00000100) != 0 || len == 0)
                return false;

            char[] buf = new char[len + 2];
            if (NativeMethods.CM_Get_Device_ID_ListW(null, buf, len, 0x00000100) != 0)
                return false;

            int start = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i] == '\0')
                {
                    int segLen = i - start;
                    if (segLen == 0) break;
                    string id = new string(buf, start, segLen);
                    if (id.StartsWith(filterPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        uint devInst;
                        if (NativeMethods.CM_Locate_DevNodeW(out devInst, id, NativeMethods.CM_LOCATE_DEVNODE_NORMAL) == 0)
                        {
                            uint st, prob;
                            if (NativeMethods.CM_Get_DevNode_Status(out st, out prob, devInst, 0) == 0)
                            {
                                if ((st & NativeMethods.DN_STARTED) != 0) return true;
                            }
                        }
                    }
                    start = i + 1;
                }
            }
            return false;
        }

        private static void PollBluetoothBattery(HardwareSnapshot snap)
        {
            uint len;
            if (NativeMethods.CM_Get_Device_ID_List_SizeW(out len, null, 0x00000100) != 0 || len == 0)
                return;

            char[] buf = new char[len + 2];
            if (NativeMethods.CM_Get_Device_ID_ListW(null, buf, len, 0x00000100) != 0)
                return;

            bool activelyConnected = false;
            int batteryFound = -1;

            int start = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i] == '\0')
                {
                    int segLen = i - start;
                    if (segLen == 0) break;
                    string id = new string(buf, start, segLen);
                    if (id.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase) ||
                        id.StartsWith("BTHLE\\", StringComparison.OrdinalIgnoreCase))
                    {
                        uint devInst;
                        if (NativeMethods.CM_Locate_DevNodeW(out devInst, id, NativeMethods.CM_LOCATE_DEVNODE_NORMAL) == 0)
                        {
                            uint st, prob;
                            if (NativeMethods.CM_Get_DevNode_Status(out st, out prob, devInst, 0) == 0 &&
                                (st & NativeMethods.DN_STARTED) != 0)
                            {
                                string name = GetDevNodeString(devInst, DEVPKEY_NAME);
                                if (!string.IsNullOrEmpty(name) &&
                                    (name.IndexOf("BW06", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     name.IndexOf("Binnune", StringComparison.OrdinalIgnoreCase) >= 0))
                                {
                                    // Check true live link state on BTHENUM\DEV_... node
                                    if (id.StartsWith("BTHENUM\\DEV_", StringComparison.OrdinalIgnoreCase))
                                    {
                                        int isConn = GetDevNodeByte(devInst, DEVPKEY_Bluetooth_IsConnected);
                                        if (isConn > 0) activelyConnected = true;
                                    }

                                    int bat = GetDevNodeByte(devInst, DEVPKEY_Bluetooth_Battery);
                                    if (bat >= 0 && bat <= 100)
                                    {
                                        batteryFound = bat;
                                    }
                                }
                            }
                        }
                    }
                    start = i + 1;
                }
            }

            snap.BluetoothConnected = activelyConnected;
            if (activelyConnected && batteryFound >= 0)
            {
                snap.BluetoothBattery = batteryFound;
            }
        }

        private static string GetDevNodeString(uint devInst, PROPERTYKEY key)
        {
            uint propType;
            uint size = 512;
            byte[] buf = new byte[size];
            if (NativeMethods.CM_Get_DevNode_PropertyW(devInst, ref key, out propType, buf, ref size, 0) == 0 && size >= 2)
            {
                return Encoding.Unicode.GetString(buf, 0, (int)size).TrimEnd('\0');
            }
            return null;
        }

        private static int GetDevNodeByte(uint devInst, PROPERTYKEY key)
        {
            uint propType;
            uint size = 16;
            byte[] buf = new byte[size];
            if (NativeMethods.CM_Get_DevNode_PropertyW(devInst, ref key, out propType, buf, ref size, 0) == 0 && size >= 1)
            {
                return buf[0];
            }
            return -1;
        }

        private static void PollCoreAudio(HardwareSnapshot snap)
        {
            IMMDeviceEnumerator enumerator = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
                // Speakers (eRender = 0, DEVICE_STATE_ACTIVE = 1)
                IMMDeviceCollection renderCol;
                if (enumerator.EnumAudioEndpoints(0, 1, out renderCol) == 0 && renderCol != null)
                {
                    uint count;
                    renderCol.GetCount(out count);
                    for (uint i = 0; i < count; i++)
                    {
                        IMMDevice dev;
                        if (renderCol.Item(i, out dev) == 0 && dev != null)
                        {
                            string fname = GetEndpointFriendlyName(dev);
                            if (fname != null && (fname.IndexOf("BW06", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  fname.IndexOf("Binnune", StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                snap.AudioEndpointFound = true;
                                snap.ActiveDeviceName = fname;
                                object oMeter;
                                if (dev.Activate(ref IID_IAudioMeterInformation, 23, IntPtr.Zero, out oMeter) == 0 && oMeter != null)
                                {
                                    float peak;
                                    ((IAudioMeterInformation)oMeter).GetPeakValue(out peak);
                                    if (peak > snap.SpeakerPeak) snap.SpeakerPeak = peak;
                                    Marshal.ReleaseComObject(oMeter);
                                }
                                object oVol;
                                if (dev.Activate(ref IID_IAudioEndpointVolume, 23, IntPtr.Zero, out oVol) == 0 && oVol != null)
                                {
                                    float vol;
                                    bool muted;
                                    var epv = (IAudioEndpointVolume)oVol;
                                    if (epv.GetMasterVolumeLevelScalar(out vol) == 0) snap.SpeakerVolume = vol;
                                    if (epv.GetMute(out muted) == 0) snap.SpeakerMuted = muted;
                                    Marshal.ReleaseComObject(oVol);
                                }
                            }
                            Marshal.ReleaseComObject(dev);
                        }
                    }
                    Marshal.ReleaseComObject(renderCol);
                }

                // Microphone (eCapture = 1, DEVICE_STATE_ACTIVE = 1)
                IMMDeviceCollection capCol;
                if (enumerator.EnumAudioEndpoints(1, 1, out capCol) == 0 && capCol != null)
                {
                    uint count;
                    capCol.GetCount(out count);
                    for (uint i = 0; i < count; i++)
                    {
                        IMMDevice dev;
                        if (capCol.Item(i, out dev) == 0 && dev != null)
                        {
                            string fname = GetEndpointFriendlyName(dev);
                            if (fname != null && (fname.IndexOf("BW06", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  fname.IndexOf("Binnune", StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                object oMeter;
                                if (dev.Activate(ref IID_IAudioMeterInformation, 23, IntPtr.Zero, out oMeter) == 0 && oMeter != null)
                                {
                                    float peak;
                                    ((IAudioMeterInformation)oMeter).GetPeakValue(out peak);
                                    if (peak > snap.MicPeak) snap.MicPeak = peak;
                                    Marshal.ReleaseComObject(oMeter);
                                }
                            }
                            Marshal.ReleaseComObject(dev);
                        }
                    }
                    Marshal.ReleaseComObject(capCol);
                }
            }
            catch
            {
            }
            finally
            {
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }

        private static string GetEndpointFriendlyName(IMMDevice dev)
        {
            IPropertyStore store = null;
            try
            {
                if (dev.OpenPropertyStore(0, out store) == 0 && store != null)
                {
                    PROPVARIANT pv;
                    PROPERTYKEY key = PKEY_FriendlyName;
                    if (store.GetValue(ref key, out pv) == 0)
                    {
                        string val = (pv.vt == 31 && pv.p1 != IntPtr.Zero) ? Marshal.PtrToStringUni(pv.p1) : null;
                        NativeMethods.PropVariantClear(ref pv);
                        return val;
                    }
                }
            }
            catch { }
            finally
            {
                if (store != null) Marshal.ReleaseComObject(store);
            }
            return null;
        }
    }

    #endregion

    #region Persistent Battery State & Discharge Engine

    public class BatteryState
    {
        // Official Binnune BW06 spec: 500mAh -> ~48h in 2.4GHz mode (@ 70% vol), ~63h in BT mode
        public const double RatedHours24G = 48.0;
        public const double RatedHoursBT = 63.0;

        public double BatteryPercent = 100.0;
        public double ActiveAudioHoursSinceSync = 0.0;
        public DateTime LastSyncLocal = DateTime.Now;
        public string LastSyncSource = "Inicial (100%)";
        public bool Alerted20 = false;
        public bool Alerted10 = false;

        private static string GetStateFilePath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BW06BatteryMonitor");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "battery_state.ini");
        }

        public static BatteryState Load()
        {
            var s = new BatteryState();
            try
            {
                string path = GetStateFilePath();
                if (File.Exists(path))
                {
                    foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        string line = raw.Trim();
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim();
                        string v = line.Substring(eq + 1).Trim();
                        double dv;
                        if (k == "BatteryPercent" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dv))
                            s.BatteryPercent = Math.Max(0.0, Math.Min(100.0, dv));
                        else if (k == "ActiveAudioHoursSinceSync" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dv))
                            s.ActiveAudioHoursSinceSync = Math.Max(0.0, dv);
                        else if (k == "LastSyncTicks")
                        {
                            long ticks;
                            if (long.TryParse(v, out ticks)) s.LastSyncLocal = new DateTime(ticks, DateTimeKind.Local);
                        }
                        else if (k == "LastSyncSource")
                            s.LastSyncSource = v;
                        else if (k == "Alerted20")
                            s.Alerted20 = (v == "1");
                        else if (k == "Alerted10")
                            s.Alerted10 = (v == "1");
                    }
                }
                else
                {
                    s.Save();
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("BatteryPercent=" + BatteryPercent.ToString("F4", CultureInfo.InvariantCulture));
                sb.AppendLine("ActiveAudioHoursSinceSync=" + ActiveAudioHoursSinceSync.ToString("F4", CultureInfo.InvariantCulture));
                sb.AppendLine("LastSyncTicks=" + LastSyncLocal.Ticks.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("LastSyncSource=" + (LastSyncSource ?? ""));
                sb.AppendLine("Alerted20=" + (Alerted20 ? "1" : "0"));
                sb.AppendLine("Alerted10=" + (Alerted10 ? "1" : "0"));
                File.WriteAllText(GetStateFilePath(), sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public void Calibrate(double newPercent, string source)
        {
            BatteryPercent = Math.Max(0.0, Math.Min(100.0, newPercent));
            ActiveAudioHoursSinceSync = 0.0;
            LastSyncLocal = DateTime.Now;
            LastSyncSource = source;
            if (BatteryPercent > 20.5) Alerted20 = false;
            if (BatteryPercent > 10.5) Alerted10 = false;
            Save();
        }
    }

    #endregion

    #region Modern Dark Popup Dashboard Form

    public class DashboardForm : Form
    {
        private readonly TrayController _controller;
        private Rectangle _chkStartupRect;
        private Rectangle _btnCloseRect;
        private int _hoverZone = 0;

        public DashboardForm(TrayController controller)
        {
            _controller = controller;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(15, 19, 28);
            ClientSize = new Size(350, 372);

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            _btnCloseRect = new Rectangle(314, 12, 24, 24);
            _chkStartupRect = new Rectangle(22, 278, 306, 26);

            Deactivate += delegate { Hide(); };
        }

        public void ShowNearTray()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 14, wa.Bottom - Height - 14);
            Show();
            Activate();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int z = 0;
            if (_btnCloseRect.Contains(e.Location)) z = 1;
            else if (_chkStartupRect.Contains(e.Location)) z = 5;

            if (z != _hoverZone)
            {
                _hoverZone = z;
                Cursor = (z > 0) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;

            if (_btnCloseRect.Contains(e.Location))
            {
                Hide();
            }
            else if (_chkStartupRect.Contains(e.Location))
            {
                _controller.ToggleRunAtStartup();
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int w = ClientSize.Width;
            int h = ClientSize.Height;

            using (var bgBrush = new SolidBrush(Color.FromArgb(15, 19, 28)))
                g.FillRectangle(bgBrush, 0, 0, w, h);

            using (var borderPen = new Pen(Color.FromArgb(42, 52, 74), 1.5f))
                g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);

            // Header
            using (var titleFont = new Font("Segoe UI Semibold", 15.5f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var subFont = new Font("Segoe UI", 11.5f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var whiteBrush = new SolidBrush(Color.FromArgb(241, 245, 249)))
            using (var statusBrush = new SolidBrush(_controller.GetConnectionStatusColor()))
            {
                g.DrawString("Binnune BW06-BT", titleFont, whiteBrush, 20, 14);
                g.DrawString(_controller.GetConnectionStatusText(), subFont, statusBrush, 20, 36);
            }

            // Close button
            if (_hoverZone == 1)
            {
                using (var hb = new SolidBrush(Color.FromArgb(45, 55, 75)))
                    FillRoundRect(g, hb, _btnCloseRect, 6);
            }
            using (var closePen = new Pen(Color.FromArgb(148, 163, 184), 1.8f))
            {
                g.DrawLine(closePen, _btnCloseRect.Left + 7, _btnCloseRect.Top + 7, _btnCloseRect.Right - 7, _btnCloseRect.Bottom - 7);
                g.DrawLine(closePen, _btnCloseRect.Right - 7, _btnCloseRect.Top + 7, _btnCloseRect.Left + 7, _btnCloseRect.Bottom - 7);
            }

            // Circular Battery Gauge
            int pct = (int)Math.Round(_controller.State.BatteryPercent);
            Color accent = _controller.GetBatteryAccentColor(pct);
            Rectangle ringRect = new Rectangle((w - 134) / 2, 68, 134, 134);

            using (var trackPen = new Pen(Color.FromArgb(30, 39, 56), 11f))
                g.DrawArc(trackPen, ringRect, 0, 360);

            float sweep = (float)(360.0 * Math.Max(0.0, Math.Min(100.0, _controller.State.BatteryPercent)) / 100.0);
            if (sweep > 0.5f)
            {
                using (var arcPen = new Pen(accent, 11f))
                {
                    arcPen.StartCap = LineCap.Round;
                    arcPen.EndCap = LineCap.Round;
                    g.DrawArc(arcPen, ringRect, -90f, sweep);
                }
            }

            using (var bigFont = new Font("Segoe UI", 31f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var remFont = new Font("Segoe UI", 11.2f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var whiteBrush = new SolidBrush(Color.FromArgb(248, 250, 252)))
            using (var mutedBrush = new SolidBrush(Color.FromArgb(148, 163, 184)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(pct + "%", bigFont, whiteBrush, new RectangleF(ringRect.X, ringRect.Y + 40, ringRect.Width, 38), sf);
                g.DrawString(_controller.GetRemainingHoursShortText(), remFont, mutedBrush, new RectangleF(ringRect.X - 20, ringRect.Y + 80, ringRect.Width + 40, 22), sf);
            }

            // Telemetry Info Card
            Rectangle infoCard = new Rectangle(22, 214, w - 44, 50);
            using (var cardBrush = new SolidBrush(Color.FromArgb(23, 29, 43)))
                FillRoundRect(g, cardBrush, infoCard, 8);

            using (var infoFont = new Font("Segoe UI", 11.0f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var textBrush = new SolidBrush(Color.FromArgb(203, 213, 225)))
            using (var dimBrush = new SolidBrush(Color.FromArgb(125, 141, 166)))
            {
                string usageLine = string.Format("Áudio ativo medido: {0}  •  Volume: {1}%",
                    FormatHours(_controller.State.ActiveAudioHoursSinceSync),
                    (int)Math.Round(_controller.LastSnapshot.SpeakerVolume * 100f));
                string syncLine = _controller.LastSnapshot.BluetoothConnected && _controller.LastSnapshot.BluetoothBattery >= 0
                    ? string.Format("Sensor Bluetooth ao vivo: {0}% (leitura direta)", _controller.LastSnapshot.BluetoothBattery)
                    : string.Format("Última leitura do sensor BT: {0:dd/MM HH:mm}", _controller.State.LastSyncLocal);
                g.DrawString(usageLine, infoFont, textBrush, 32, 221);
                g.DrawString(syncLine, infoFont, dimBrush, 32, 240);
            }

            // Live Audio Meter Bar at bottom of infoCard
            float meterPct = Math.Min(1.0f, Math.Max(_controller.LastSnapshot.SpeakerPeak, _controller.LastSnapshot.MicPeak) * 1.8f);
            if (meterPct > 0.01f)
            {
                int barW = (int)((infoCard.Width - 16) * meterPct);
                if (barW > 2)
                {
                    using (var meterBrush = new SolidBrush(Color.FromArgb(52, 211, 153)))
                        g.FillRectangle(meterBrush, infoCard.X + 8, infoCard.Bottom - 3, barW, 2);
                }
            }

            // Startup Checkbox
            bool runStartup = _controller.IsRunAtStartupEnabled();
            Rectangle box = new Rectangle(_chkStartupRect.X + 2, _chkStartupRect.Y + 5, 16, 16);
            using (var boxBrush = new SolidBrush(runStartup ? Color.FromArgb(59, 130, 246) : Color.FromArgb(30, 39, 56)))
                FillRoundRect(g, boxBrush, box, 4);
            if (runStartup)
            {
                using (var ckPen = new Pen(Color.White, 1.8f))
                {
                    g.DrawLine(ckPen, box.X + 3, box.Y + 8, box.X + 7, box.Y + 12);
                    g.DrawLine(ckPen, box.X + 7, box.Y + 12, box.X + 13, box.Y + 4);
                }
            }
            using (var chkFont = new Font("Segoe UI", 11.8f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var chkBrush = new SolidBrush(_hoverZone == 5 ? Color.White : Color.FromArgb(203, 213, 225)))
            {
                g.DrawString("Iniciar automaticamente com o Windows", chkFont, chkBrush, box.Right + 8, _chkStartupRect.Y + 4);
            }

            // Footer Auto-Monitoring Status
            Rectangle tipRect = new Rectangle(22, 312, w - 44, 46);
            using (var tipBg = new SolidBrush(Color.FromArgb(20, 30, 48)))
                FillRoundRect(g, tipBg, tipRect, 7);
            using (var tipFont = new Font("Segoe UI", 10.3f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var tipBrush = new SolidBrush(Color.FromArgb(147, 197, 253)))
            {
                g.DrawString(
                    "Monitoramento 100% automático: leitura direta do sensor no\n" +
                    "Bluetooth e cálculo contínuo de consumo de áudio no USB 2.4GHz.",
                    tipFont, tipBrush, new RectangleF(tipRect.X + 8, tipRect.Y + 6, tipRect.Width - 14, tipRect.Height - 8));
            }
        }

        private static string FormatHours(double hours)
        {
            int totalMin = (int)Math.Round(hours * 60.0);
            return string.Format("{0}h {1:D2}m", totalMin / 60, totalMin % 60);
        }

        private static void FillRoundRect(Graphics g, Brush brush, Rectangle r, int radius)
        {
            int d = radius * 2;
            using (var gp = new GraphicsPath())
            {
                gp.AddArc(r.X, r.Y, d, d, 180, 90);
                gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                gp.CloseFigure();
                g.FillPath(brush, gp);
            }
        }
    }

    #endregion

    #region System Tray Controller

    public class TrayController : ApplicationContext
    {
        private const string StartupRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "BW06BatteryMonitor";

        public BatteryState State { get; private set; }
        public HardwareSnapshot LastSnapshot { get; private set; }

        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _miStartup;
        private readonly ToolStripMenuItem _miStatusHeader;
        private readonly DashboardForm _dashboard;
        private readonly System.Windows.Forms.Timer _pollTimer;
        private readonly EventWaitHandle _showUiEvent;

        private IntPtr _currentIconHandle = IntPtr.Zero;
        private int _lastRenderedIconKey = -999;
        private DateTime _lastTickUtc = DateTime.UtcNow;
        private DateTime _lastAudioActiveUtc = DateTime.MinValue;
        private DateTime _lastSaveUtc = DateTime.UtcNow;
        private int _tickCounter = 0;
        private bool _wasBtConnected = false;
        private int _lastSyncedBtValue = -1;

        public TrayController(EventWaitHandle showUiEvent)
        {
            _showUiEvent = showUiEvent;
            State = BatteryState.Load();
            LastSnapshot = BW06HardwareProbe.Poll(true);
            if ((LastSnapshot.SpeakerPeak > 0.0015f && !LastSnapshot.SpeakerMuted) || LastSnapshot.MicPeak > 0.005f)
            {
                _lastAudioActiveUtc = DateTime.UtcNow;
            }

            _menu = new ContextMenuStrip();
            _miStatusHeader = new ToolStripMenuItem("BW06-BT") { Enabled = false };
            _menu.Items.Add(_miStatusHeader);
            _menu.Items.Add(new ToolStripSeparator());

            var miOpen = new ToolStripMenuItem("Abrir Painel BW06-BT");
            miOpen.Font = new Font(miOpen.Font, FontStyle.Bold);
            miOpen.Click += delegate { ShowDashboard(); };
            _menu.Items.Add(miOpen);

            _menu.Items.Add(new ToolStripSeparator());
            _miStartup = new ToolStripMenuItem("Iniciar junto com o Windows");
            _miStartup.Checked = IsRunAtStartupEnabled();
            _miStartup.Click += delegate { ToggleRunAtStartup(); };
            _menu.Items.Add(_miStartup);

            _menu.Items.Add(new ToolStripSeparator());
            var miExit = new ToolStripMenuItem("Sair");
            miExit.Click += delegate { ExitMonitor(); };
            _menu.Items.Add(miExit);

            _trayIcon = new NotifyIcon();
            _trayIcon.ContextMenuStrip = _menu;
            _trayIcon.Visible = true;
            _trayIcon.MouseClick += OnTrayMouseClick;

            _dashboard = new DashboardForm(this);

            UpdateTrayVisuals(true);

            _pollTimer = new System.Windows.Forms.Timer();
            _pollTimer.Interval = 2000; // Every 2 seconds
            _pollTimer.Tick += OnPollTick;
            _pollTimer.Start();

            // Listen for second-instance activation signal
            if (_showUiEvent != null)
            {
                Task.Run(() =>
                {
                    while (true)
                    {
                        _showUiEvent.WaitOne();
                        if (_dashboard != null && !_dashboard.IsDisposed)
                        {
                            try { _dashboard.BeginInvoke((MethodInvoker)delegate { ShowDashboard(); }); } catch { }
                        }
                    }
                });
            }

            _trayIcon.ShowBalloonTip(
                3500,
                "Monitor BW06-BT Ativo",
                string.Format("Bateria estimada: {0:0}% ({1}). Clique no ícone ao lado do relógio para abrir o painel.",
                    Math.Round(State.BatteryPercent), GetRemainingHoursShortText()),
                ToolTipIcon.Info);
        }

        private void OnTrayMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (_dashboard.Visible) _dashboard.Hide();
                else ShowDashboard();
            }
        }

        public void ShowDashboard()
        {
            _dashboard.ShowNearTray();
        }

        private void OnPollTick(object sender, EventArgs e)
        {
            DateTime nowUtc = DateTime.UtcNow;
            double dtHours = (nowUtc - _lastTickUtc).TotalHours;
            _lastTickUtc = nowUtc;
            if (dtHours < 0.0 || dtHours > 0.05) dtHours = 2.0 / 3600.0; // Ignore system sleep jumps

            _tickCounter++;
            bool checkBt = (_tickCounter % 2 == 0); // Every 4 seconds
            HardwareSnapshot snap = BW06HardwareProbe.Poll(checkBt);
            if (!checkBt)
            {
                snap.BluetoothConnected = LastSnapshot.BluetoothConnected;
                snap.BluetoothBattery = LastSnapshot.BluetoothBattery;
            }
            LastSnapshot = snap;

            // 1. Check Bluetooth Real-Hardware Sync
            if (snap.BluetoothConnected && snap.BluetoothBattery >= 0 && snap.BluetoothBattery <= 100)
            {
                int b = snap.BluetoothBattery;
                bool liveStepChanged = (_wasBtConnected && _lastSyncedBtValue >= 0 && b != _lastSyncedBtValue);
                bool outOfCurrentStepBand = (State.BatteryPercent > b) || (State.BatteryPercent < b - 9.0);

                _wasBtConnected = true;
                _lastSyncedBtValue = b;

                if (liveStepChanged || outOfCurrentStepBand)
                {
                    State.Calibrate(b, "Bluetooth (Real)");
                    _trayIcon.ShowBalloonTip(
                        4000,
                        "BW06-BT Sincronizado via Bluetooth!",
                        string.Format("Leitura real do sensor capturada: {0}% ({1}).",
                            b, GetRemainingHoursShortText()),
                        ToolTipIcon.Info);
                }
            }
            else if (!snap.BluetoothConnected)
            {
                _wasBtConnected = false;
                _lastSyncedBtValue = -1;
            }

            // 2. Discharge calculation (both 2.4GHz USB and smooth interpolation between BT 10% hardware steps)
            if (snap.DongleConnected || snap.BluetoothConnected)
            {
                bool spkPlaying = snap.AudioEndpointFound && !snap.SpeakerMuted && snap.SpeakerPeak > 0.0012f;
                bool micActive = snap.MicPeak > 0.006f;

                if (spkPlaying || micActive)
                {
                    _lastAudioActiveUtc = nowUtc;
                }

                double secSinceAudio = (nowUtc - _lastAudioActiveUtc).TotalSeconds;
                if (secSinceAudio <= 45.0)
                {
                    double ratedHours = snap.BluetoothConnected ? BatteryState.RatedHoursBT : BatteryState.RatedHours24G;
                    double baseRatePerHour = 100.0 / ratedHours;
                    double volFactor = 0.75 + (0.36 * snap.SpeakerVolume); // ~1.0 at 70% vol
                    double micExtra = micActive ? 0.18 : 0.0;
                    double drainPerHour = (baseRatePerHour * volFactor) + micExtra;

                    // In BT mode, don't drift more than 9% below the last real 10%-step hardware report
                    double floor = (snap.BluetoothConnected && _lastSyncedBtValue >= 10)
                        ? Math.Max(0.0, _lastSyncedBtValue - 9.0)
                        : 0.0;

                    State.BatteryPercent = Math.Max(floor, State.BatteryPercent - (drainPerHour * dtHours));
                    State.ActiveAudioHoursSinceSync += dtHours;
                }
                else if (secSinceAudio <= 900.0)
                {
                    double floor = (snap.BluetoothConnected && _lastSyncedBtValue >= 10)
                        ? Math.Max(0.0, _lastSyncedBtValue - 9.0)
                        : 0.0;
                    State.BatteryPercent = Math.Max(floor, State.BatteryPercent - (0.12 * dtHours));
                }
            }

            // 3. Low-battery alerts
            CheckLowBatteryAlerts();

            // 4. Periodic state persistence (every 60s)
            if ((nowUtc - _lastSaveUtc).TotalSeconds >= 60.0)
            {
                State.Save();
                _lastSaveUtc = nowUtc;
            }

            UpdateTrayVisuals(false);
            if (_dashboard.Visible) _dashboard.Invalidate();
        }

        private void CheckLowBatteryAlerts()
        {
            if (State.BatteryPercent <= 10.0 && !State.Alerted10)
            {
                State.Alerted10 = true;
                State.Alerted20 = true;
                State.Save();
                _trayIcon.ShowBalloonTip(
                    5000,
                    "⚠️ Bateria Crítica — BW06-BT (10%)",
                    "Restam cerca de 4h30 de uso. Conecte o cabo USB-C em breve!",
                    ToolTipIcon.Warning);
            }
            else if (State.BatteryPercent <= 20.0 && !State.Alerted20)
            {
                State.Alerted20 = true;
                State.Save();
                _trayIcon.ShowBalloonTip(
                    4500,
                    "🔋 Bateria Baixa — BW06-BT (20%)",
                    "Sua carga estimada chegou a 20% (~9h30 restantes).",
                    ToolTipIcon.Warning);
            }
        }

        public bool IsAudioCurrentlyActive()
        {
            return (DateTime.UtcNow - _lastAudioActiveUtc).TotalSeconds <= 45.0;
        }

        public string GetConnectionStatusText()
        {
            if (LastSnapshot.BluetoothConnected)
                return "🔵 Conectado via Bluetooth (Bateria Real)";
            if (!LastSnapshot.DongleConnected)
                return "⚪ Adaptador USB 2.4GHz desconectado";
            if (IsAudioCurrentlyActive())
                return "🟢 2.4GHz USB — Em uso (Áudio ativo)";
            return "🟡 2.4GHz USB — Em silêncio / Standby";
        }

        public Color GetConnectionStatusColor()
        {
            if (LastSnapshot.BluetoothConnected)
                return Color.FromArgb(96, 165, 250);
            if (!LastSnapshot.DongleConnected)
                return Color.FromArgb(148, 163, 184);
            if (IsAudioCurrentlyActive())
                return Color.FromArgb(52, 211, 153);
            return Color.FromArgb(251, 191, 36);
        }

        public string GetRemainingHoursShortText()
        {
            double rated = LastSnapshot.BluetoothConnected ? BatteryState.RatedHoursBT : BatteryState.RatedHours24G;
            double remHours = (State.BatteryPercent / 100.0) * rated;
            int h = (int)Math.Floor(remHours);
            int m = (int)Math.Round((remHours - h) * 60.0);
            if (m == 60) { h++; m = 0; }
            return string.Format("~{0}h {1:D2}m restantes", h, m);
        }

        public Color GetBatteryAccentColor(int pct)
        {
            if (!LastSnapshot.DongleConnected && !LastSnapshot.BluetoothConnected)
                return Color.FromArgb(148, 163, 184);
            if (pct <= 20) return Color.FromArgb(248, 113, 113);
            if (pct <= 50) return Color.FromArgb(251, 191, 36);
            return Color.FromArgb(52, 211, 153);
        }

        private void UpdateTrayVisuals(bool forceIcon)
        {
            int pct = (int)Math.Round(State.BatteryPercent);
            bool connected = LastSnapshot.DongleConnected || LastSnapshot.BluetoothConnected;
            int iconKey = (connected ? 1000 : 0) + pct;

            string modeShort = LastSnapshot.BluetoothConnected
                ? "Bluetooth (Real)"
                : (!LastSnapshot.DongleConnected ? "Adaptador desconectado"
                    : (IsAudioCurrentlyActive() ? "2.4GHz (Em uso)" : "2.4GHz (Standby)"));

            string tip = string.Format("BW06-BT: {0}% ({1})\n{2}", pct, GetRemainingHoursShortText(), modeShort);
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            _trayIcon.Text = tip;
            _miStatusHeader.Text = string.Format("BW06-BT: {0}%  •  {1}", pct, GetRemainingHoursShortText());

            if (forceIcon || iconKey != _lastRenderedIconKey)
            {
                _lastRenderedIconKey = iconKey;
                IntPtr newHIcon = RenderBatteryTrayIcon(pct, connected);
                if (newHIcon != IntPtr.Zero)
                {
                    _trayIcon.Icon = Icon.FromHandle(newHIcon);
                    if (_currentIconHandle != IntPtr.Zero)
                    {
                        NativeMethods.DestroyIcon(_currentIconHandle);
                    }
                    _currentIconHandle = newHIcon;
                }
            }
        }

        private IntPtr RenderBatteryTrayIcon(int pct, bool connected)
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                Color accent = GetBatteryAccentColor(pct);

                // Top battery bar indicator
                using (var track = new SolidBrush(Color.FromArgb(60, 72, 95)))
                    g.FillRectangle(track, 2, 2, 26, 6);
                using (var tip = new SolidBrush(Color.FromArgb(140, 155, 180)))
                    g.FillRectangle(tip, 28, 3, 2, 4);

                int fillW = Math.Max(2, (int)Math.Round(24.0 * Math.Max(0, Math.Min(100, pct)) / 100.0));
                using (var fill = new SolidBrush(accent))
                    g.FillRectangle(fill, 3, 3, fillW, 4);

                // Percentage text (large and crisp on taskbar, DPI-independent pixel sizing)
                string label = pct >= 100 ? "100" : pct.ToString();
                float fontSize = pct >= 100 ? 13.5f : 17.5f;
                using (var f = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var tb = new SolidBrush(connected ? Color.White : Color.FromArgb(180, 190, 205)))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(label, f, tb, new RectangleF(-2, 9, 36, 23), sf);
                }

                return bmp.GetHicon();
            }
        }

        public bool IsRunAtStartupEnabled()
        {
            try
            {
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(StartupRunKey, false))
                {
                    if (rk == null) return false;
                    object v = rk.GetValue(StartupValueName);
                    return v != null && v.ToString().Length > 0;
                }
            }
            catch { return false; }
        }

        public void ToggleRunAtStartup()
        {
            try
            {
                bool enable = !IsRunAtStartupEnabled();
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(StartupRunKey, true))
                {
                    if (rk != null)
                    {
                        if (enable)
                            rk.SetValue(StartupValueName, "\"" + Application.ExecutablePath + "\"");
                        else
                            rk.DeleteValue(StartupValueName, false);
                    }
                }
                _miStartup.Checked = enable;
            }
            catch { }
        }

        private void ExitMonitor()
        {
            _pollTimer.Stop();
            State.Save();
            _trayIcon.Visible = false;
            if (_currentIconHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_currentIconHandle);
                _currentIconHandle = IntPtr.Zero;
            }
            _trayIcon.Dispose();
            Application.Exit();
        }
    }

    #endregion

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // CLI diagnostic mode (--status) for instant verification
            if (args != null && args.Length > 0 && args[0] == "--status")
            {
                NativeMethods.AttachConsole(-1);
                var snap = BW06HardwareProbe.Poll(true);
                string msg = string.Format(
                    "Dongle24G={0} AudioEndpoint={1} Device='{2}' SpkPeak={3:F4} SpkVol={4:0}% Muted={5} MicPeak={6:F4} BTConnected={7} BTBattery={8}",
                    snap.DongleConnected, snap.AudioEndpointFound, snap.ActiveDeviceName,
                    snap.SpeakerPeak, snap.SpeakerVolume * 100f, snap.SpeakerMuted,
                    snap.MicPeak, snap.BluetoothConnected, snap.BluetoothBattery);
                Console.WriteLine(msg);
                if (args.Length > 1) File.WriteAllText(args[1], msg, Encoding.UTF8);
                return;
            }

            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\BW06BatteryMonitor_SingleInstance", out createdNew))
            {
                var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\BW06BatteryMonitor_ShowUI");
                if (!createdNew)
                {
                    showEvent.Set();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayController(showEvent));
            }
        }
    }
}
