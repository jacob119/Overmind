﻿using System;
using System.IO;
using System.Collections.Generic;
using NX.Hooks;

namespace NX_Overmind
{
    public class LogItemCodec
    {
        /**
         * TOTAL        :   8 + 32  =   40bits/entry    =   5bytes/entry
         * BIT PACKING  :   {Class:1}   {Type:3}    {ModifierKeys:4     }        {Extra:8}   {Extra:8}    {Extra:8}      {Extra:8}    {Extra:8}      {Extra:8}
         *                  |           HEADER                          |        |               EXTRA                                                       |
         *                  
         * KeyType      :   0           1,2,4       Alt,Ctrl,Shift,Caps          |VK_CODE_1   VK_CODE_2    VK_CODE_3       CHAR  |      -              -
         * MouseType    :   1           1-6         Alt,Ctrl,Shift,Caps          |      X            |    |         Y            |     Button         Click/Scroll  
         * 
         */

        public enum Offsets : byte
        {
            KeyLogOffset = 5,
            MouseLogOffset = 7
        }

        public enum EventClass : uint
        {            
            KeyEvent = 0,
            MouseEvent = 1,
            None    = 2
        }

        public enum EventType : uint
        {
            KeyDown = 0x01,
            KeyPress = 0x02,
            KeyUp = 0x04,

            MouseDown = 0x01,
            MouseClick = 0x02,
            MouseUp = 0x03,
            MouseDblClick = 0x04,
            MouseMove = 0x05,
            MouseWheel = 0x06,

            None    = 0xFF
        }

        public static byte[] GetEncodedData(EventClass eventClass, EventType type, HookArgs args, int[] pressedKeys=null, char pressedChar='\0')
        {
            byte[] data = null;            
            using (MemoryStream ms = new MemoryStream())
            {                                
                ms.WriteByte(GetHeaderByte(eventClass, type, args));
                if (eventClass == EventClass.MouseEvent)
                {
                    MouseHookEventArgs me = args as MouseHookEventArgs;
                    ms.Write(BitConverter.GetBytes((ushort)me.Location.X),0 , 2);
                    ms.Write(BitConverter.GetBytes((ushort)me.Location.Y), 0, 2);
                    //ms.WriteByte((byte)me.Location.X);
                    //ms.WriteByte((byte)me.Location.Y);
                    //System.Windows.Forms.MouseButtons.Right;

                    ms.WriteByte((byte)((int)me.Button >> 20));
                    byte extra = 0;
                    switch (type)
                    {
                        case EventType.MouseClick:
                        case EventType.MouseDblClick:
                            extra = (byte)me.ClickCount;
                            break;
                        case EventType.MouseWheel:
                            extra = (byte)Math.Abs(me.ScrollAmount);
                            if (me.ScrollAmount < 0)
                                extra |= 0x80;                            
                            break;
                    }
                    ms.WriteByte(extra);
                }
                else
                {
                    KeyHookEventArgs ke = args as KeyHookEventArgs;                    
                    byte[] pKeys = { 0, 0, 0 };
                    int len = Math.Min(3, pressedKeys.Length);
                    for (int i = 0; i < len; i++)
                        pKeys[i] = (byte)pressedKeys[i];

                    ms.Write(pKeys, 0, 3);
                    ms.WriteByte((byte)pressedChar);
                }

                data = ms.ToArray();
            }
            return data;
        }

        public static bool GetDecodedData(int offset, byte[] data, out EventClass eventClass, out EventType type, out HookArgs args, out byte[] pressedKeys, out char pressedChar)
        {            
            using (MemoryStream ms = new MemoryStream(data))
            {
                // Set Offset
                ms.Position = offset;

                // Assign
                pressedKeys = null;
                pressedChar = '\0';
                eventClass = EventClass.None;
                type = EventType.None;
                args = null;

                // Check
                if (ms.Position < ms.Length)
                {
                    FromHeaderByte((byte)ms.ReadByte(), out eventClass, out type, out args);
                    pressedKeys = new byte[3] { 0, 0, 0 };                    

                    if (eventClass == EventClass.MouseEvent)
                    {

                        //int x = ms.ReadByte();
                        //int y = ms.ReadByte();
                        byte[] loc = new byte[2];

                        ms.Read(loc, 0, 2);
                        int x = BitConverter.ToInt16(loc, 0);
                        ms.Read(loc, 0, 2);
                        int y = BitConverter.ToInt16(loc, 0);
                        System.Windows.Forms.MouseButtons button = System.Windows.Forms.MouseButtons.None;
                        switch(ms.ReadByte())
                        {
                            case 1:
                                button = System.Windows.Forms.MouseButtons.Left;
                                break;
                            case 2:
                                button = System.Windows.Forms.MouseButtons.Right;
                                break;
                            case 4:
                                button = System.Windows.Forms.MouseButtons.Middle;
                                break;
                            case 8:
                                button = System.Windows.Forms.MouseButtons.XButton1;
                                break;
                            case 16:
                                button = System.Windows.Forms.MouseButtons.XButton2;
                                break;
                        }
                        int extra = ms.ReadByte();
                        int click = 0;
                        int scroll = 0;
                        switch (type)
                        {
                            case EventType.MouseClick:
                            case EventType.MouseDblClick:
                                click = extra;
                                break;
                            case EventType.MouseWheel:
                                if ((extra & 0x80) == 0x80)
                                    scroll = -1;
                                else
                                    scroll = 1;
                                
                                extra &= 0x7F;
                                scroll = scroll * extra * 120; //Delta
                                break;
                        }
                        
                        args = new MouseHookEventArgs(false, button, click, x, y, scroll, args.Alt, args.Control, args.Shift, args.CapsLock, args.NumLock, args.ScrollLock);
                    }
                    else
                    {
                        ms.Read(pressedKeys, 0, 3);
                        pressedChar = (char)ms.ReadByte();
                        args = new KeyHookEventArgs(pressedKeys[0], pressedChar, false, args.Alt, args.Control, args.Shift, args.CapsLock, args.NumLock, args.ScrollLock);
                    }
                    return true;
                }                
            }
            return false;
        }

        private static byte GetHeaderByte(EventClass ec, EventType et, HookArgs ha)
        {
            byte head = 0;
            if(ec == EventClass.MouseEvent)
                head = 0x80;

            head |= (byte)((uint)et << 4);

            if (ha.Alt)
                head |= 0x08;
            if(ha.Control)
                head |= 0x04;
            if (ha.Shift)
                head |= 0x02;
            if (ha.CapsLock)
                head |= 0x01;

            return head;
        }

        private static void FromHeaderByte(byte head, out EventClass ec, out EventType et, out HookArgs ha)
        {
            if ((head & 0x80) == 0x80)
                ec = EventClass.MouseEvent;
            else
                ec = EventClass.KeyEvent;

            et = (EventType)((head & 0x70) >> 4);

            ha = new HookArgs((head & 0x08) == 0x08, (head & 0x04) == 0x04, (head & 0x02) == 0x02, (head & 0x01) == 0x01, false, false);
        }
            
    }
}
