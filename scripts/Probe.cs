using System;
using RazerHelper;
using System.Web.Script.Serialization;

internal static class Probe
{
    static int Main()
    {
        try { Console.WriteLine(new JavaScriptSerializer().Serialize(new { Model = HidDiscovery.Model(), Endpoints = HidDiscovery.Enumerate() })); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
