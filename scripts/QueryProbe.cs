using System;
using System.Linq;
using RazerHelper;

internal static class QueryProbe
{
    static int Main()
    {
        try
        {
            if (!HidDiscovery.IsSupportedModel(HidDiscovery.Model())) throw new Exception("Unknown model");
            var endpoint = HidDiscovery.Enumerate().First(x => x.Product == 0x02c5 && x.FeatureLength == 91 && x.Path.Contains("&mi_02"));
            using (var transport = new HidTransport(endpoint))
            {
                Query(transport, 0x0081, new byte[] { 0, 0 });
                Query(transport, 0x0383, new byte[] { 1, 5, 0 });
                Query(transport, 0x0e84, new byte[] { 1, 0 });
                Query(transport, 0x0f82, new byte[] { 1, 5, 0 });
                Query(transport, 0x0d82, new byte[] { 0, 1, 0, 0 });
                Query(transport, 0x0d82, new byte[] { 0, 2, 0, 0 });
                Query(transport, 0x0d88, new byte[] { 0, 1, 0 });
                Query(transport, 0x0d88, new byte[] { 0, 2, 0 });
                Query(transport, 0x0d87, new byte[] { 0, 1, 0 });
                Query(transport, 0x0d87, new byte[] { 0, 2, 0 });
                Query(transport, 0x0d81, new byte[] { 0, 1, 0 });
                Query(transport, 0x0d81, new byte[] { 0, 2, 0 });
                Query(transport, 0x0792, new byte[] { 0 });
                Query(transport, 0x0084, new byte[] { 0, 0 });
            }
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static void Query(IReportTransport transport, ushort command, byte[] args)
    {
        var report = new byte[91]; report[2] = 0x1f; report[6] = (byte)args.Length; report[7] = (byte)(command >> 8); report[8] = (byte)command;
        Array.Copy(args, 0, report, 9, args.Length);
        for (int i = 3; i < 89; i++) report[89] ^= report[i];
        try { var result = transport.Exchange(report); Console.WriteLine(command.ToString("X4") + " " + BitConverter.ToString(result)); }
        catch (Exception e) { Console.WriteLine(command.ToString("X4") + " ERROR " + e.Message); }
    }
}
