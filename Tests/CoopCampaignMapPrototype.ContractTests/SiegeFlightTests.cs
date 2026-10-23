using System;
using CoopSpectator.Infrastructure;

internal static class SiegeFlightTests
{
    internal static void Run()
    {
        var flight = new CoopCampaignMapSiegeFlight {
            StartHours = 10, EndHours = 11, Origin = new float[] { 1,2,3 },
            Forward = new float[] { 0,1,0 }, Up = new float[] { 0,0,1 },
            Speed = 14, Gravity = 10, LaunchAngle = 0
        };
        Check(flight.TryPosition(10.5, out var position), "mid-flight admitted");
        Check(Math.Abs(position[1] - 9) < 0.0001 && Math.Abs(position[2] - 1.75) < 0.0001,
            "native ballistic position");
        Check(!flight.TryPosition(10, out _) && !flight.TryPosition(11, out _) &&
            !flight.TryPosition(12, out _), "strict native flight interval; no extrapolation");
        Check(!flight.TryPosition(double.NaN, out _), "invalid time");
        Check(flight.TryDirection(10.5, out var direction), "flight direction admitted");
        double chordLength = Math.Sqrt(14 * 14 + 5.5 * 5.5);
        Check(Math.Abs(direction[1] - 14 / chordLength) < 0.000001 &&
            Math.Abs(direction[2] + 5.5 / chordLength) < 0.000001,
            "native forward chord, not instantaneous tangent");
        Check(!flight.TryDirection(10, out _) && !flight.TryDirection(11, out _) &&
            !flight.TryDirection(double.NaN, out _), "direction obeys display interval");
        Check(flight.TryDirection(10.99, out _), "native look-ahead may cross impact time");
        flight.Forward = new float[] { 1,0,0 };
        Check(flight.TryDirection(10.5, out direction) && direction[0] > 0 && direction[1] == 0,
            "direction follows supplied world axes");
        flight.Forward = new float[] { 0,1,0 };
        var nativeUp = new float[] { 0,0,2 };
        var nativeScale = new float[] { 2,3,4 };
        Check(flight.TryFrame(10.5, nativeUp, nativeScale, out var frame), "scaled flight frame");
        Check(Math.Abs(frame[0] - 2) < 0.000001 &&
            Math.Abs(frame[4] - 3 * 14 / chordLength) < 0.000001 &&
            Math.Abs(frame[5] + 3 * 5.5 / chordLength) < 0.000001 &&
            Math.Abs(frame[7] - 4 * 5.5 / chordLength) < 0.000001 &&
            Math.Abs(frame[8] - 4 * 14 / chordLength) < 0.000001 && frame[10] == 9,
            "native cross-product order and independent local-axis scales");
        Check(nativeUp[2] == 2 && nativeScale[2] == 4, "frame inputs remain detached");
        Check(!flight.TryFrame(10.5, new float[3], nativeScale, out _) &&
            !flight.TryFrame(10.5, nativeUp, new float[] { 0,1,1 }, out _) &&
            !flight.TryFrame(10.5, nativeUp, new float[] { 1,float.NaN,1 }, out _),
            "invalid frame basis and scale rejected");
        Check(!flight.TryFrame(11, nativeUp, nativeScale, out _), "frame stops at impact boundary");
        flight.EndHours = 10.01;
        Check(flight.TryPosition(10.005, out _), "short flight independent of publication interval");
        flight.Up = new float[] { 0,1,0 };
        Check(!flight.TryPosition(10.005, out _), "parallel axes rejected");
        flight.Up = new float[] { 0,0,1 }; flight.Gravity = float.PositiveInfinity;
        Check(!flight.TryPosition(10.005, out _), "invalid gravity rejected");
        flight.Gravity = 10;
        flight.PresentationScale = new float[] { 2,3,4 };
        Check(CoopCampaignMapSiegeFlightCodec.TryEncode(flight, out var encoded) && encoded.Length == 81, "fixed wire shape");
        Check(CoopCampaignMapSiegeFlightCodec.TryDecode(encoded, out var decoded) &&
            decoded.TryPosition(10.005, out var decodedPosition) && flight.TryPosition(10.005, out var originalPosition) &&
            Math.Abs(decodedPosition[1] - originalPosition[1]) < 0.0001 && decoded.PresentationScale[2] == 4,
            "trajectory and native scale wire roundtrip");
        for (int length = 0; length < encoded.Length; length++)
            Check(!CoopCampaignMapSiegeFlightCodec.TryDecode(new byte[length], out _), "truncated wire rejected");
        Check(!CoopCampaignMapSiegeFlightCodec.TryDecode(new byte[82], out _), "trailing bytes rejected");
        var corrupted = (byte[])encoded.Clone(); corrupted[0] ^= 1;
        Check(!CoopCampaignMapSiegeFlightCodec.TryDecode(corrupted, out _), "wrong format rejected");
        corrupted = (byte[])encoded.Clone(); Array.Copy(BitConverter.GetBytes(double.NaN), 0, corrupted, 4, 8);
        Check(!CoopCampaignMapSiegeFlightCodec.TryDecode(corrupted, out _), "invalid wire time rejected");
        var buffer = new CoopCampaignMapSiegeFlightBuffer();
        var old = buffer.Begin(); var current = buffer.Begin();
        Check(!buffer.TryAdd(old, 1, flight), "old batch cannot publish");
        System.Threading.Tasks.Parallel.For(1, 257, id => {
            Check(buffer.TryAdd(current, id, flight), "concurrent detached submission");
        });
        flight.Origin[0] = 99;
        Check(buffer.TryFinish(current, out var batch) && batch.Count == 256, "complete parallel batch");
        Check(batch[1].Origin[0] == 1, "submission deep copy");
        Check(!buffer.TryAdd(current, 257, flight), "closed batch rejected");
        current = buffer.Begin();
        Check(buffer.TryAdd(current, 1, flight) && !buffer.TryAdd(current, 1, flight), "duplicate fails batch");
        Check(!buffer.TryFinish(current, out _), "no partial duplicate batch");
        current = buffer.Begin();
        for (int i = 1; i <= 256; i++) Check(buffer.TryAdd(current, i, flight), "bounded fill");
        Check(!buffer.TryAdd(current, 257, flight) && !buffer.TryFinish(current, out _), "overflow fails batch");
        current = buffer.Begin(); buffer.Clear();
        Check(!buffer.TryFinish(current, out _), "lifecycle clear revokes token");
        current = buffer.Begin();
        Check(buffer.TryFinish(current, out batch) && batch.Count == 0, "valid empty batch");
        var journal = new CoopCampaignMapSiegeFlightJournal();
        var epoch = new object();
        var observations = new System.Collections.Generic.Dictionary<long, CoopCampaignMapSiegeFlight> { { 1, flight } };
        Check(journal.Merge(epoch, 1, observations), "journal records flight");
        flight.Origin[0] = 55;
        Check(journal.Merge(epoch, 1.1, new System.Collections.Generic.Dictionary<long, CoopCampaignMapSiegeFlight>()), "empty next tick");
        Check(journal.Snapshot(epoch, 1.2).Count == 1 && journal.Snapshot(epoch, 1.2)[1].Origin[0] == 99,
            "short completed flight retained as copied delivery evidence");
        Check(journal.Snapshot(epoch, 3.01).Count == 0, "retention bounded");
        Check(journal.Snapshot(new object(), 1.2).Count == 0, "foreign siege cannot read history");
        Check(!journal.Merge(epoch, 0.5, observations) && journal.Snapshot(epoch, 1.2).Count == 0, "clock reversal revokes");
        Check(journal.Merge(epoch, 2, observations), "journal reopens");
        journal.Clear();
        Check(journal.Snapshot(epoch, 2).Count == 0, "visibility revocation clears history");
        Console.WriteLine("Siege flight mathematical foundation passed; capture/render not connected.");
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception("Siege flight: " + message); }
}
