﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Globalization;

namespace x86CS
{
    public partial class MainForm : Form
    {
        private Machine machine;
        private string[] screenText = new string[25];
        private int currLine, currPos;
        Font panelFont = new Font("Courier New", 9.64f);
        bool clearDebug = true;
        bool stepping = false;
        Thread machineThread;
        Breakpoints breakpoints = new Breakpoints();

        public MainForm()
        {
            machine = new Machine();
            machine.WriteText += new EventHandler<TextEventArgs>(machine_WriteText);
            machine.WriteChar += new EventHandler<CharEventArgs>(machine_WriteChar);

            breakpoints.ItemAdded += new EventHandler<IntEventArgs>(breakpoints_ItemAdded);
            breakpoints.ItemDeleted += new EventHandler<IntEventArgs>(breakpoints_ItemDeleted);

            currLine = currPos = 0;

            InitializeComponent();

            PrintRegisters();

            mainPanel.Select();

            machineThread = new Thread(new ThreadStart(RunMachine));
            machineThread.Start();

            for (int i = 0; i < screenText.Length; i++)
            {
                screenText[i] = new string(' ', 80);
            }
        }

        void breakpoints_ItemDeleted(object sender, IntEventArgs e)
        {
            machine.ClearBreakpoint(e.Number);
        }

        void breakpoints_ItemAdded(object sender, IntEventArgs e)
        {
            machine.SetBreakpoint(e.Number);
        }

        private void RunMachine()
        {
            while (true)
            {
                if (machine.Running && !stepping)
                {
                    if (machine.CheckBreakpoint())
                    {
                        stepping = true;
                        machine.CPU.Debug = true;
                        this.Invoke((MethodInvoker)delegate { PrintRegisters(); });
                    }
                    else
                        machine.CPU.Debug = false;

                    machine.RunCycle();
                }
            }
        }

        void machine_WriteChar(object sender, CharEventArgs e)
        {
            switch (e.Char)
            {
                case '\r':
                    currPos = 0;
                    break;
                case '\n':
                    currLine++;
                    break;
                default:
                    char[] chars = screenText[currLine].ToCharArray();

                    chars[currPos] = e.Char;

                    screenText[currLine] = new string(chars);
                    currPos++;
                    break;
            }

            if (currPos == 80)
            {
                currPos = 0;
                currLine++;
            }

            if (currLine >= 24)
            {
                currLine = 0;
                currPos = 0;
            }

            mainPanel.Invalidate();
        }

        private void SetCPULabel(string text)
        {
            cpuLabel.Text = text;
        }

        void CPU_DebugText(object sender, TextEventArgs e)
        {
            this.Invoke((MethodInvoker)delegate { SetCPULabel(e.Text); });
        }

        void mainPanel_Paint(object sender, PaintEventArgs e)
        {
            if (e.ClipRectangle.Height == 0)
                return;
            for (int i = 0; i < 25; i++)
            {
                string line = screenText[i];
                if (String.IsNullOrEmpty(line))
                    continue;
                e.Graphics.DrawString(line, panelFont, Brushes.White, new PointF(0, i * panelFont.Height * 1.06f));
            }
        }

        void machine_WriteText(object sender, TextEventArgs e)
        {
            screenText[currLine++] = e.Text;
            if (currLine >= 25)
                currLine = 0;

            mainPanel.Invalidate();
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            machine.Stop();
            machineThread.Abort();
            Application.Exit();
        }

        private void PrintRegisters()
        {
            CPU cpu = machine.CPU;

            EAX.Text = cpu.EAX.ToString("X8");
            EBX.Text = cpu.EBX.ToString("X8");
            ECX.Text = cpu.ECX.ToString("X8");
            EDX.Text = cpu.EDX.ToString("X8");
            ESI.Text = cpu.ESI.ToString("X8");
            EDI.Text = cpu.EDI.ToString("X8");
            EBP.Text = cpu.EBP.ToString("X8");
            ESP.Text = cpu.ESP.ToString("X8");
            CS.Text = cpu.CS.ToString("X4");
            DS.Text = cpu.DS.ToString("X4");
            ES.Text = cpu.ES.ToString("X4");
            FS.Text = cpu.FS.ToString("X4");
            GS.Text = cpu.GS.ToString("X4");
            SS.Text = cpu.SS.ToString("X4");

            CF.Text = cpu.CF ? "CF" : "cf";
            PF.Text = cpu.PF ? "PF" : "pf";
            AF.Text = cpu.AF ? "AF" : "af";
            ZF.Text = cpu.ZF ? "ZF" : "zf";
            SF.Text = cpu.SF ? "SF" : "sf";
            TF.Text = cpu.TF ? "TF" : "tf";
            IF.Text = cpu.IF ? "IF" : "if";
            DF.Text = cpu.DF ? "DF" : "df";
            OF.Text = cpu.OF ? "OF" : "of";
            IOPL.Text = cpu.IOPL.ToString("X2");
            AC.Text = cpu.AC ? "AC" : "ac";
            NT.Text = cpu.NT ? "NT" : "nt";
            RF.Text = cpu.RF ? "RF" : "rf";
            VM.Text = cpu.VM ? "VM" : "vm";
            VIF.Text = cpu.VIF ? "VIF" : "vif";
            VIP.Text = cpu.VIP ? "VIP" : "vip";   
        }

        private void runToolStripMenuItem_Click(object sender, EventArgs e)
        {
            runToolStripMenuItem.Enabled = false;
            stopToolStripMenuItem.Enabled = true;
            machine.Start();
        }

        private void stopToolStripMenuItem_Click(object sender, EventArgs e)
        {
            stopToolStripMenuItem.Enabled = false;
            runToolStripMenuItem.Enabled = true;
            machine.Stop();
        }

        private void mountToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (floppyOpen.ShowDialog() != DialogResult.OK)
                return;

            machine.FloppyDrive.MountImage(floppyOpen.FileName);
        }

        private void stepButton_Click(object sender, EventArgs e)
        {
            stepping = true;

            if (!machine.Running)
            {
                machine.Start();
                SetCPULabel(machine.Operation);
                PrintRegisters();
                return;
            }

            machine.CPU.Debug = true;
            machine.RunCycle();
            SetCPULabel(machine.Operation);
            PrintRegisters();
        }

        private void goButton_Click(object sender, EventArgs e)
        {
            if (!machine.Running)
                machine.Start();

            machine.CPU.Debug = false;
            stepping = false;
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            machineThread.Abort();
        }

        private void mainPanel_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            machine.KeyPresses.Push((char)e.KeyValue);
        }

        private void mainPanel_Click(object sender, EventArgs e)
        {
            mainPanel.Select();
        }

        private void memoryButton_Click(object sender, EventArgs e)
        {
            ushort seg = 0;
            ushort off = 0;
            uint addr;

            try
            {
                seg = ushort.Parse(memSegment.Text, NumberStyles.HexNumber);
                off = ushort.Parse(memOffset.Text, NumberStyles.HexNumber);
            }
            catch
            {
            }
                
            addr = (uint)((seg << 4) + off);

            memByte.Text = Memory.ReadByte(addr).ToString("X2");
            memWord.Text = Memory.ReadWord(addr).ToString("X4");
        }

        private void breakpointsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            breakpoints.ShowDialog();
        }

        private void restartToolStripMenuItem_Click(object sender, EventArgs e)
        {
            machine.Restart();

            machine.CPU.Debug = false;
            stepping = false;
        }
    }
}
