using OP1wBatteryTray;

static byte[] Reply(byte status, byte percent)
{
    var result = new byte[64];
    result[0] = 0xA1;
    result[1] = status;
    result[16] = percent;
    result[17] = 0xCA;
    result[18] = 0x0E;
    return result;
}

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine($"PASS: {name}");
}

Check(BatteryReader.ParseResponse(Reply(0x08, 80), "test") is null, "cached 80% is rejected when mouse is unreachable");
Check(BatteryReader.ParseResponse(Reply(0x03, 80), "test") is null, "busy reply is rejected");
Check(BatteryReader.ParseResponse(Reply(0x01, 101), "test") is null, "invalid percentage is rejected");
Check(BatteryReader.ParseResponse(new byte[4], "test") is null, "short reply is rejected");
var replies = new Queue<byte[]>(new[] { Reply(0x01, 80), Reply(0x01, 45) });
var waited = 0;
var fresh = BatteryReader.ReadFreshResponse(() => replies.Dequeue(), ms => waited += ms);
var reading = BatteryReader.ParseResponse(fresh!, "test");
Check(replies.Count == 0 && waited == 100 && reading?.Percent == 45 && reading.VoltageMillivolts == 3786,
    "old first response is discarded and fresh 45% is returned");
replies = new Queue<byte[]>(new[] { Reply(0x01, 45), Reply(0x08, 80) });
fresh = BatteryReader.ReadFreshResponse(() => replies.Dequeue(), _ => { });
Check(BatteryReader.ParseResponse(fresh!, "test") is null, "fresh first reply cannot override an unreachable second reply");
Check(BatteryReader.ReadFreshResponse(() => null, _ => throw new Exception("unexpected delay")) is null,
    "failed exchange returns no reading");

namespace OP1wBatteryTray
{
    internal static class AppLog
    {
        internal static void Write(string message) { }
    }
}
