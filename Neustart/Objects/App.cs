﻿using System;
using System.Text;
using System.Timers;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Newtonsoft.Json;

namespace Neustart
{
    [JsonObject(MemberSerialization.OptIn)]
    public class App
    {
        private static List<ProcessPriorityClass> Priorities = new List<ProcessPriorityClass>()
        {
            ProcessPriorityClass.Idle,
            ProcessPriorityClass.BelowNormal,
            ProcessPriorityClass.Normal,
            ProcessPriorityClass.AboveNormal,
            ProcessPriorityClass.High,
            ProcessPriorityClass.RealTime
        };

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool EnableWindow(IntPtr hwnd, bool enable);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowTextLength(HandleRef hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(HandleRef hWnd, StringBuilder lpString, int nMaxCount);

        [JsonProperty]
        public string ID { get; set; }
        [JsonProperty]
        public bool Enabled { get; set; }
        [JsonProperty]
        public bool Hidden { get; set; } = false;
        [JsonProperty]
        public string Path { get; set; }
        [JsonProperty]
        public string Args { get; set; }
        [JsonProperty]
        public int Affinities { get; set; }
        [JsonProperty]
        public int Priority { get; set; } = 2;
        [JsonProperty]
        public int PID { get; set; } = -1;
        [JsonProperty]
        public DateTime StartTime { get; set; }
        [JsonProperty]
        public int Crashes { get; set; } = 0;

        public string WindowName { get; set; }
        public Process Process { get; set; }

        public DataGridViewRow DataRow { get; set; }

        public int FrozenInterval = 0;

        private Thread RestartThread;

        public bool IsRestarting = false;

        public void Init()
        {
            WindowName = ID;

            if (Enabled)
                Start();

            DataRow.Cells[2].Value = Crashes;

            RestartThread = new Thread(HandleCrashes);
            RestartThread.Start();
        }

        public bool Start()
        {
            try {
                bool resumed = false;

                if (PID != -1)
                {
                    try
                    {
                        Process = Process.GetProcessById(PID);

                       // if (Process.StartTime != StartTime)
                        //    throw new Exception();

                        resumed = true;
                    } catch { } // We'll have to start a new instance

                    PID = -1;
                    StartTime = DateTime.MinValue;
                }

                if (!resumed)
                {
                    ProcessStartInfo info = new ProcessStartInfo();
                    info.WorkingDirectory = System.IO.Path.GetDirectoryName(Path);
                    info.Arguments = Args;
                    info.FileName = Path;

                    Process = Process.Start(info);

                    Process.ProcessorAffinity = (IntPtr)Affinities;
                    Process.PriorityClass = Priorities[Priority];

                    Process.WaitForInputIdle();

                }

                StartTime = Process.StartTime;
                PID = Process.Id;

                HandleHide();

                DataRow.Cells[6].Value = "Stop";

                IsRestarting = false;

                return true;
            }
            catch (Exception)
            {
                if (Enabled)
                    MessageBox.Show("An error occurred while starting " + ID + ". It has been disabled.", "Neustart");

                Enabled = false;
                IsRestarting = false;
                DataRow.Cells[6].Value = "Start";

                return false;
            }
        }

        private void HandleCrashes()
        {
            while (true)
            {
                if (Enabled && (IsClosed() || IsCrashed()))
                {
                    IsRestarting = true;

                    AddCrash();
                    Program.SaveAppData();

                    Thread.Sleep(2000); // Delay a restart, in the past there have been issues with sockets not being freed if we don't wait.

                    if (Enabled && (IsClosed() || IsCrashed()))
                        Start();
                }

                Thread.Sleep(100);
            }
        }

        public void Close()
        {
            if (RestartThread != null)
                RestartThread.Abort();
        }

        public bool IsClosed()
            => (Process == null) || Process.HasExited;

        public bool IsCrashed()
        {
            if (Process == null)
                return false;

            if (Process.Responding)
                FrozenInterval = 0;
            else
                FrozenInterval++;

            return FrozenInterval >= 10;
        }

        public void Stop()
        {
            DataRow.Cells[1].Value = ID;
            DataRow.Cells[6].Value = "Start";

            IsRestarting = false;

            if (IsClosed())
                return;

            Process.Kill();

            Process = null;
        }

        public string GetProcessName()
        {
            var processes = Process.GetProcessesByName(Process.ProcessName);

            int count = -1;
            foreach (Process proc in processes)
            {
                count++;
                if (proc.Id == Process.Id)
                {
                    if (count == 0)
                        return Process.ProcessName;
                    else
                        return Process.ProcessName + "#" + count;
                }
            }
            return Process.ProcessName + "#" + processes.Length;
        }

        public void GetTitle()
        {
            if (IsRestarting)
            {
                WindowName = "Restarting...";
            }
            else if (!IsClosed())
            {
                IntPtr hwnd = Process.MainWindowHandle;

                int capacity = GetWindowTextLength(new HandleRef(this, hwnd)) * 2;
                StringBuilder sBuilder = new StringBuilder(capacity);

                GetWindowText(new HandleRef(this, hwnd), sBuilder, sBuilder.Capacity);

                WindowName = sBuilder.ToString();
            }
            else
            {
                WindowName = ID;
            }

            DataRow.Cells[1].Value = WindowName;
        }

        public void GetUptime()
        {
            if (Process != null)
            {
                double total = (DateTime.Now - Process.StartTime).TotalSeconds;
                double hours = Math.Floor(total / 3600);
                double minutes = Math.Floor((total % 3600) / 60);
                double seconds = Math.Floor(total - (hours * 3600) - (minutes * 60));

                if (minutes < 10)
                    minutes = 0;

                if (seconds < 10)
                    seconds = 0;

                DataRow.Cells[3].Value = hours + ":" + minutes + ":" + seconds;
            } 
        }

        public void RefreshProcess()
        {
            if (Process != null)
                Process.Refresh();
        }

        public void GetCPU()
        {
            if (Process != null)
            {
                decimal thiscputime = Process.PrivilegedProcessorTime.Milliseconds;
                decimal programcputime = Program.CpuMilliseconds - thiscputime;
                DataRow.Cells[4].Value = Convert.ToString((Program.CpuMilliseconds > 0) ? Math.Round((thiscputime / programcputime) * 100, 1) : 1) + "%";
            }
        }

        public void GetRam()
        {
            if (Process != null)
                DataRow.Cells[5].Value = (Process.PrivateMemorySize64 / 1048576) + " MB";
        }

        public bool ToggleHide()
        {
            if (!Enabled)
                return Hidden;

            Hidden = !Hidden;

            HandleHide();

            DataRow.Cells[7].Value = Hidden ? "Show" : "Hide";

            return Hidden;
        }

        private void HandleHide()
        {
            if (Process == null)
                return;

            IntPtr hwnd = Process.MainWindowHandle;

            ShowWindow(hwnd, Hidden ? 0 : 1);
            EnableWindow(hwnd, Hidden ? false : true);
        }

        public void ResetCrashes()
        {
            Crashes = 0;
            DataRow.Cells[2].Value = Crashes;
        }

        public void AddCrash()
        {
            Crashes++;
            DataRow.Cells[2].Value = Crashes;
        }
    }
}
