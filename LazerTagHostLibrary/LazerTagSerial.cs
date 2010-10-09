
using System;
using System.Collections.Generic;

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
            System.IO.DirectoryInfo di = new System.IO.DirectoryInfo("/dev");
            System.IO.FileInfo[] fi = di.GetFiles("ttyUSB*");

            foreach (System.IO.FileInfo f in fi) {
                result.Add(f.FullName);
            }

            return result;
        }
    }
}
