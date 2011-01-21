
using System;
using System.Collections.Generic;
using System.IO.Ports;

namespace LazerTagHostLibrary
{


    public class LazerTagSerial
    {

        public LazerTagSerial ()
        {
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
