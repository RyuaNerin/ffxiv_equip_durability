using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ffxiv_equip_durability
{
    public partial class frmMain : Form
    {
        private static class NativeMethods
        {
            public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

            [DllImport("user32.dll")]
            public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
            
            [DllImport("user32.dll")]
            public static extern bool ReleaseCapture();

            [DllImport("user32.dll")]
            public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

            [DllImport("user32.dll")]
            public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

            [DllImport("user32.dll")]
            public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

            [DllImport("user32.dll")]
            public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

            [DllImport("user32.dll", SetLastError=true)]
            public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

            [DllImport("user32.dll")]
            public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
            
            [DllImport("user32.dll")]
            public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

            [DllImport("kernel32.dll", CallingConvention = CallingConvention.Winapi)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsWow64Process([In] IntPtr process, [Out] out bool wow64Process);
            
		    [DllImport("kernel32.dll")]
		    public static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

		    [DllImport("kernel32.dll")]
		    public static extern Int32 CloseHandle(IntPtr hProcess);

		    [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

            public const int WS_EX_LAYERED = 0x80000;
            public const int WS_EX_TOOLWINDOW = 0x80;
            public const int WM_NCLBUTTONDOWN = 0xA1;
            public const int HT_CAPTION = 0x2;

            public const int EVENT_SYSTEM_MENUEND = 0x5;
            public const int EVENT_SYSTEM_FOREGROUND = 0x3;

            public const int WINEVENT_OUTOFCONTEXT = 0;
            public const int WINEVENT_SKIPOWNPROCESS = 2;
            
            public static bool IsX64(Process process)
            {
                var version = Environment.OSVersion.Version;
                if ((version.Major > 5) || ((version.Major == 5) && (version.Minor >= 1)))
                {
                    bool retVal;
                    return !(NativeMethods.IsWow64Process(process.Handle, out retVal) && retVal);
                }

                return false; // not on 64-bit Windows Emulator
            }

            public static byte[] ReadMemory(byte[] buff, IntPtr hproc, IntPtr addr, int numOfBytes)
            {
                int bytesRead;
                NativeMethods.ReadProcessMemory(hproc, addr, buff, numOfBytes, out bytesRead);

                return buff;
            }
        }

        readonly NativeMethods.WinEventDelegate m_hookProc;
        readonly Label[] m_dura = new Label[13];
        readonly Label[] m_spir = new Label[13];
        Process m_proc;
        IntPtr m_hproc;
        IntPtr m_hhook;
        
        public frmMain()
        {
            InitializeComponent();
            
            this.m_hookProc = new NativeMethods.WinEventDelegate(this.WinEventProc);
        }

        private void frmMain_FormClosed(object sender, FormClosedEventArgs e)
        {
            NativeMethods.UnhookWinEvent(this.m_hhook);
            NativeMethods.CloseHandle(this.m_hproc);
        }

        private void frmMain_Load(object sender, EventArgs e)
        {
//             int initialStyle = NativeMethods.GetWindowLong(this.Handle, -20);
//             NativeMethods.SetWindowLong(this.Handle, -20, initialStyle | 0x80000 | 0x20);

            this.Width = 130;

            int y = 2;
            for (int i = 0; i < m_dura.Length; ++i)
            {
                this.m_spir[i] = new Label
                {
                    AutoSize = false,
                    Left = 8,
                    Top = y,
                    Width = 55,
                    Height = 20,
                    Text = "0.0",
                    TextAlign = ContentAlignment.MiddleRight,
                };

                this.m_dura[i] = new Label
                {
                    AutoSize = false,
                    Left = 70,
                    Top = y,
                    Width = 55,
                    Height = 20,
                    Text = "0.0",
                    TextAlign = ContentAlignment.MiddleRight,
                };
                
                if (i != this.m_dura.Length)
                    y += 11;

                if (i == 1)
                    y += 6;
                else if (i == 7)
                    y += 6;

                y += 10;


                this.Controls.Add(this.m_dura[i]);
                this.Controls.Add(this.m_spir[i]);
            }

            this.Height = y + 2;

            Task.Factory.StartNew(this.Worker);

        }

        private void lblDrag_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(this.Handle, NativeMethods.WM_NCLBUTTONDOWN, NativeMethods.HT_CAPTION, 0);
            }
        }
        
        private void Worker()
        {
            int pid;
            IntPtr ptr;
            byte[] buff = new byte[0x40 * 13];
            int i;
            int offset;

            while (true)
            {
                if ((this.m_proc == null) || this.m_proc.HasExited)
                {
                    NativeMethods.UnhookWinEvent(this.m_hhook);
                    NativeMethods.CloseHandle(this.m_hproc);
                    this.m_proc = null;

                    ptr = NativeMethods.FindWindow("FFXIVGAME", null);
                    if (ptr != IntPtr.Zero)
                    {
                        if (NativeMethods.GetWindowThreadProcessId(ptr, out pid) != 0)
                        {
                            this.m_proc = Process.GetProcessById(pid);
                            if (this.m_proc != null)
                            {
                                this.m_hproc = NativeMethods.OpenProcess(0x00000010, false, this.m_proc.Id);
                                if (this.m_hproc != IntPtr.Zero)
                                {
                                    this.Invoke(new Action(
                                        () => this.m_hhook = NativeMethods.SetWinEventHook(
                                            NativeMethods.EVENT_SYSTEM_FOREGROUND,
                                            NativeMethods.EVENT_SYSTEM_FOREGROUND,
                                            IntPtr.Zero,
                                            this.m_hookProc,
                                            0,
                                            0,
                                            NativeMethods.WINEVENT_OUTOFCONTEXT)
                                        ));

                                    continue;
                                }
                            }
                        }
                    }

                    this.m_proc = null;

                    Thread.Sleep(10 * 1000);
                }
                else
                {
                    ptr = m_proc.MainModule.BaseAddress + 0x01DAC748;
                    ptr = new IntPtr(BitConverter.ToInt64(NativeMethods.ReadMemory(buff, this.m_hproc, ptr, 8), 0)) + 0x60;
                    ptr = new IntPtr(BitConverter.ToInt64(NativeMethods.ReadMemory(buff, this.m_hproc, ptr, 8), 0));
                    
                    NativeMethods.ReadMemory(buff, this.m_hproc, ptr, 0x38 * 13);

                    for (i = 0; i < 13; ++i)
                    {
                        offset = i * 0x38;
                        if (BitConverter.ToUInt16(buff, offset + 0x08) == 0)
                            this.SetValue(i, -1, -1);
                        else
                            this.SetValue(i, BitConverter.ToUInt16(buff, offset + 0x10), BitConverter.ToUInt16(buff, offset + 0x12));
                    }
                    
                    Thread.Sleep(1000);
                }
            }
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != 0 || idChild != 0)
                return;

            NativeMethods.SetWindowPos(this.Handle, new IntPtr(/*HWND_TOPMOST*/-1), 0, 0, 0, 0, /*SWP_NOMOVE | SWP_NOSIZE*/ 3);
        }

        private void SetValue(int index, int spir, int dura)
        {
            if (this.InvokeRequired)
                this.Invoke(new Action<int, int, int>(this.SetValue), index, spir, dura);
            else
            {
                var dura_p = Math.Floor(dura / 300d * 10) / 10;

                this.m_spir[index].Text = (spir == -1) ? "-" : string.Format("{0:##0.0}", Math.Floor(spir / 100d * 10) / 10);
                this.m_dura[index].Text = (dura == -1) ? "-" : string.Format("{0:##0.0}", dura_p);

                if (dura < 10)
                {
                    this.m_dura[index].ForeColor = Color.Red;
                } else
                {
                    this.m_dura[index].ForeColor = SystemColors.ControlText;
                }
            }
        }
    }
}
