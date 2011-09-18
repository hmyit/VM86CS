﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace x86CS
{
    public class TextEventArgs : EventArgs
    {
        private string text;

        public TextEventArgs(string textToWrite)
        {
            text = textToWrite;
        }

        public string Text
        {
            get { return text; }
            set { text = value; }
        }
    }

    public class CharEventArgs : EventArgs
    {
        private char ch;

        public CharEventArgs(char charToWrite)
        {
            ch = charToWrite;
        }

        public char Char
        {
            get { return ch; }
            set { ch = value; }
        }
    }

    public class IntEventArgs : EventArgs
    {
        private int number;

        public IntEventArgs(int num)
        {
            number = num;
        }

        public int Number
        {
            get { return number; }
            set { number = value; }
        }
    }

    public delegate void InteruptHandler();

    public struct SystemConfig
    {
        public ushort NumBytes;
        public byte Model;
        public byte SubModel;
        public byte Revision;
        public byte Feature1;
        public byte Feature2;
        public byte Feature3;
        public byte Feature4;
        public byte Feature5;
    }

    public class Machine
    {
        private CPU cpu = new CPU();
        private Floppy floppyDrive;
        private bool running = false;
        public event EventHandler<TextEventArgs> WriteText;
        public event EventHandler<CharEventArgs> WriteChar;
        private Dictionary<int, InteruptHandler> interuptVectors = new Dictionary<int, InteruptHandler>();
        private Stack<char> keyPresses = new Stack<char>();
        private Dictionary<int, int> breakpoints = new Dictionary<int, int>();
        private SystemConfig sysConfig = new SystemConfig();

        public Stack<char> KeyPresses
        {
            get { return keyPresses; }
            set { keyPresses = value; }
        }

        public Floppy FloppyDrive
        {
            get { return floppyDrive; }
        }

        public bool Running
        {
            get { return running; }
        }

        public CPU CPU
        {
            get { return cpu; }
        }

        public Machine()
        {
            IntPtr p;
            byte[] tmp;

            floppyDrive = new Floppy();

            sysConfig.NumBytes = 8;
            sysConfig.Model = 0xfc;
            sysConfig.SubModel = 0;
            sysConfig.Revision = 0;
            sysConfig.Feature1 = 0x74;
            sysConfig.Feature2 = 0x40;
            sysConfig.Feature3 = 0;
            sysConfig.Feature4 = 0;
            sysConfig.Feature5 = 0;

            p = Marshal.AllocHGlobal(Marshal.SizeOf(sysConfig));
            Marshal.StructureToPtr(sysConfig, p, false);
            tmp = new byte[Marshal.SizeOf(sysConfig)];

            Marshal.Copy(p, tmp, 0, Marshal.SizeOf(sysConfig));
            Memory.BlockWrite(0xf0100, tmp, tmp.Length);

            Marshal.FreeHGlobal(p);

            interuptVectors.Add(0x10, Int10);
            interuptVectors.Add(0x11, Int11);
            interuptVectors.Add(0x12, Int12);
            interuptVectors.Add(0x13, Int13);
            interuptVectors.Add(0x15, Int15);
            interuptVectors.Add(0x16, _Int16);
            interuptVectors.Add(0x19, Int19);

            p = Marshal.AllocHGlobal(Marshal.SizeOf(floppyDrive.DPT));
            Marshal.StructureToPtr(floppyDrive.DPT, p, false);
            tmp = new byte[Marshal.SizeOf(floppyDrive.DPT)];
            Marshal.Copy(p, tmp, 0, tmp.Length);

            Memory.BlockWrite(0xf0000, tmp, tmp.Length);
            Memory.WriteWord(0x78, 0x0000);
            Memory.WriteWord(0x7a, 0xf000);

            Marshal.FreeHGlobal(p);

            cpu.InteruptFired += new EventHandler<IntEventArgs>(cpu_InteruptFired);
        }

        public void Restart()
        {
            cpu.Reset();
            Int19();
        }

        public void SetBreakpoint(int addr)
        {
            if (breakpoints.ContainsKey(addr))
                return;

            breakpoints.Add(addr, addr);
        }

        public void ClearBreakpoint(int addr)
        {
            if (!breakpoints.ContainsKey(addr))
                return;

            breakpoints.Remove(addr);
        }

        public bool CheckBreakpoint()
        {
            int cpuAddr = (cpu.CS << 4) + cpu.IP;

            foreach (KeyValuePair<int, int> kvp in breakpoints)
            {
                if (kvp.Value == cpuAddr)
                    return true;
            }
            return false;
        }

        private char GetChar()
        {
            if (keyPresses.Count > 0)
                return keyPresses.Pop();

            while (keyPresses.Count == 0)
                ;

            return keyPresses.Pop();
        }

        void cpu_InteruptFired(object sender, IntEventArgs e)
        {
            InteruptHandler handler;

            if (!interuptVectors.TryGetValue(e.Number, out handler))
            {
                Console.WriteLine("Missing interrupt {0:X2}", e.Number);
                return;
            }

            if (handler != null)
                handler();
        }

        private void Int10()
        {
            switch (cpu.AH)
            {
                case 0x0e:
                    DoWriteChar((char)cpu.AL);
                    break;
                default:
                    break;
            }
        }

        private void Int11()
        {
            cpu.AX = 0x27;
        }

        private void Int12()
        {
            cpu.AX = 639;
        }

        private bool ReadSector()
        {
            int count = cpu.AL;
            byte sector, cyl, head;
            DisketteParamTable dpt;
            byte[] buffer = new byte[Marshal.SizeOf(typeof(DisketteParamTable))];          
            IntPtr p;

            sector = (byte)(cpu.CL & 0x3f);
            cyl = cpu.CH;
            head = cpu.DH;

            Console.WriteLine("Sector {0:X2}, Cyl {1:X2}, Head {2:X2}, Count {3:X2}", sector, cyl, head, count);

            Memory.BlockRead((Memory.ReadWord(0x7a) << 16) + Memory.ReadWord(0x78), buffer, buffer.Length);
            p = Marshal.AllocHGlobal(buffer.Length);
            Marshal.Copy(buffer, 0, p, buffer.Length);
            dpt = (DisketteParamTable)Marshal.PtrToStructure(p, typeof(DisketteParamTable));

            int addr = (cyl * 2 + head) * dpt.LastTrack + (sector - 1);

            byte[] fileBuffer = floppyDrive.ReadSector(addr);

            Memory.SegBlockWrite(cpu.ES, cpu.BX, fileBuffer, fileBuffer.Length);

            return true;
        }

        private void Int13()
        {
            switch (cpu.AH)
            {
                case 0x00:
                    floppyDrive.Reset();
                    cpu.CF = false;
                    break;
                case 0x02:
                    if (ReadSector())
                    {
                        cpu.CF = false;
                        cpu.AH = 0;
                    }
                    else
                        cpu.CF = true;
                    break;
                default:
                    break;
            }
        }

        private void Int15()
        {
            switch (cpu.AH)
            {
                case 0xc0:
                    cpu.CF = false;
                    cpu.AH = 0;

                    cpu.ES = 0xf000;
                    cpu.BX = 0x0100;
                    break;
                default:
                    break;
            }
        }

        private void _Int16()
        {
            switch (cpu.AH)
            {
                case 0x00:
                    char c = GetChar();
                    break;
                default:
                    break;
            }
        }

        private void Int19()
        {
            bool gotBootSector = false;

            // Try and find a boot loader on the floppy drive image if there is one
            if (floppyDrive.Mounted)
            {
                byte[] bootSect;

                floppyDrive.Reset();

                bootSect = floppyDrive.ReadBytes(512);

                if (bootSect[510] == 0x55 || bootSect[511] == 0xAA)
                {
                    Memory.BlockWrite(0x07c0 << 4, bootSect, 512);
                    gotBootSector = true;
                }
                else
                {
                    DoWriteText("Non bootable Disk in floppy drive");
                }
            }

            if (gotBootSector)
            {
                cpu.SetSegment(SegmentRegister.CS, 0);
                cpu.IP = 0x7c00;
                running = true;
            }
        }

        private void DoWriteText(string text)
        {
            EventHandler<TextEventArgs> textEvent = WriteText;

            if (textEvent != null)
                textEvent(this, new TextEventArgs(text));
        }

        private void DoWriteChar(char ch)
        {
            EventHandler<CharEventArgs> charEvent = WriteChar;

            if (charEvent != null)
                charEvent(this, new CharEventArgs(ch));
        }

        public void Start()
        {
            Int19();
        }

        public void Stop()
        {
            running = false;
        }

        public void RunCycle()
        {
            if(running)
                cpu.Cycle();
        }
    }
}
