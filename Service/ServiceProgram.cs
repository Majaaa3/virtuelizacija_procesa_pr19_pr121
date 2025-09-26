using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;

namespace Service
{
    public class ServiceProgram
    {
        static void Main()
        {
            using (var host = new ServiceHost(typeof(EisIngestService)))
            {
                host.Open();
                Console.WriteLine("EisIngestService started. Press ENTER to exit...");
                Console.ReadLine();
                host.Close();
            }
        }
    }
}
