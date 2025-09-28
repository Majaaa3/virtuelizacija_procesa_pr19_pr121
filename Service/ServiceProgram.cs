using System;
using System.ServiceModel;

namespace Service
{
    public class ServiceProgram
    {
        static void Main()
        {
            // Kreiraj instancu servisa
            var service = new EisIngestService();

            // Pretplate na događaje
            service.OnTransferStarted += (sessionId) =>
            {
                Console.WriteLine($"[EVENT] Transfer started. Session={sessionId}");
            };

            service.OnSampleReceived += (sessionId, sample) =>
            {
                Console.WriteLine($"[EVENT] Sample received. Session={sessionId}, Row={sample.RowIndex}, Freq={sample.FrequencyHz}");
            };

            service.OnTransferCompleted += (sessionId) =>
            {
                Console.WriteLine($"[EVENT] Transfer completed. Session={sessionId}");
            };

            service.OnWarningRaised += (msg) =>
            {
                Console.WriteLine($"[WARNING] {msg}");
            };

            // Pokreni host sa ovom instancom servisa
            using (var host = new ServiceHost(service))
            {
                host.Open();
                Console.WriteLine("EisIngestService started. Press ENTER to exit...");
                Console.ReadLine();
                host.Close();
            }
        }
    }
}
