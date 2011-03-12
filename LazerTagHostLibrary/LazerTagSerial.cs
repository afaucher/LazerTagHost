using System;
using System.IO.Ports;
using System.Collections.Generic;

namespace LazerTagHostLibrary
{


    public class LazerTagSerial
    {
        private SerialPort serial_port = null;
        private const int INTER_PACKET_BYTE_DELAY_MILISECONDS = 100;
        private Queue<byte[]> q = null;

        System.Threading.Thread worker_thread = null;
        private Boolean run = false;

        public LazerTagSerial (string device)
        {
            if (device != null) {
                serial_port = new SerialPort(device, 115200);
                serial_port.Parity = Parity.None;
                serial_port.StopBits = StopBits.One;
                serial_port.Open();
            }

            q = new Queue<byte[]>();

            run = true;
            worker_thread = new System.Threading.Thread(WriteThread);
            worker_thread.IsBackground = true;
            worker_thread.Start();
        }

        public void Stop()
        {
            run = false;
            worker_thread.Join();
            if (serial_port != null && serial_port.IsOpen) {
                serial_port.Close();
                serial_port = null;
            }
        }

        public string TryReadCommand()
        {
            if (serial_port == null || !serial_port.IsOpen) {
                return null;
            } else if (serial_port.BytesToRead > 0) {
                string input = serial_port.ReadLine();
                
                return input;
            }
            return null;
        }

        private void WriteThread()
        {
            try {
                while (run) {
                    if (q.Count > 0 && serial_port != null) {
                        byte[] packet = q.Dequeue();
    
                        serial_port.Write( packet, 0, 2 );
                        serial_port.BaseStream.Flush();
                    }
                    System.Threading.Thread.Sleep(INTER_PACKET_BYTE_DELAY_MILISECONDS);
                }
            } catch (Exception ex) {
                System.Console.WriteLine(ex.ToString());
            }
        }

        public void EnqueueLTTO(UInt16 data, UInt16 number_of_bits)
        {
            byte[] packet = new byte[2] {
                (byte)((0x01 << 5) | ((number_of_bits & 0xf) << 1) | ((data >> 8) & 0x1)),
                (byte)(data & 0xff),
            };
            q.Enqueue(packet);
        }

        public void EnqueueLTX(UInt16 data, UInt16 number_of_bits)
        {
             byte[] packet = new byte[2] {
                (byte)((0x00 << 5) | (number_of_bits << 1) | ((data >> 8) & 0x1)),
                (byte)(data & 0xff),
            };
            q.Enqueue(packet);
        }

        public void TransmitPacket(ref UInt16[] values)
        {
            for (int i = 0; i < values.Length; i++) {
                UInt16 packet = values[i];
                EnqueueLTX(packet,(UInt16)(i == 0 ? 9 : 8));
            }
            UInt16 checksum = ComputeChecksum2(ref values);
            checksum |= 0x100;
            EnqueueLTX(checksum,9);
            System.Console.WriteLine("TX Count {0:d}", q.Count);
            String debug = "TX: ";
            foreach (UInt16 v in values) {
                debug += v.ToString() + ",";
            }
            System.Console.WriteLine(debug);

        }

        static public byte ComputeChecksum2(ref UInt16[]values)
        {
            int i = 0;
            byte sum = 0;
            for (i = 0; i < values.Length; i++) {
                sum += (byte)values[i];
            }
            return sum;
        }

        static public byte ComputeChecksum(ref List<IRPacket> values)
        {
            byte sum = 0;
            foreach (IRPacket packet in values) {
                //don't add the checksum value in
                if ((packet.data & 0x100) == 0) {
                    sum += (byte)packet.data;
                }
            }
            return sum;
        }

        static public List<string> GetSerialPorts()
        {
            List<string> result = new List<string>();
            try {
                System.IO.DirectoryInfo di = new System.IO.DirectoryInfo("/dev");
                System.IO.FileInfo[] fi = di.GetFiles("ttyUSB*");
    
                foreach (System.IO.FileInfo f in fi) {
                    result.Add(f.FullName);
                }
            } catch (Exception) {
                //eh
            }

            try {
                String[] ports = SerialPort.GetPortNames();
                foreach (String p in ports) {
                    result.Add(p);
                }
            } catch (Exception) {
                //eh
            }

            return result;
        }
    }
}
