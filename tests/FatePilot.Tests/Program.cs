using FatePilot.Core;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

var tests = new List<(string, Action)>();
void Test(string name, Action run) => tests.Add((name, run));
void Assert(bool result, string message = "Assertion failed") { if (!result) throw new Exception(message); }
void Throws(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected refusal"); }
var home = new Destination(129, 8, "Limsa");
var zone1 = new Destination(1000, 100, "Zone A");
var zone2 = new Destination(1001, 101, "Zone B");
var fate = new FateInfo(1, "Combat FATE", 1000, EventState.Running, 90, 94, 0, 600, new(100, 0, 0), 50, true, false, 1234);
var world = new World { LoggedIn = true, Territory = 1000, Job = 22, Level = 100, NavReady = true, Position = Vector3.Zero, Fates = [fate], Instance = 1, InstanceCount = 3 };
RunOptions Options() => new() { ReturnDestination = home, ReturnAfterFates = 1, UseMount = false, IdleSeconds = 10 };
(Runner Runner, Fake Runtime) Started(World? w = null, RunOptions? o = null)
{
    var fake = new Fake(); var runner = new Runner(fake); runner.Start(o ?? Options(), w ?? world, 0);
    Assert(runner.IsRunning, runner.Status); return (runner, fake);
}
World Engage(Runner r)
{
    r.Tick(world, 1); r.Tick(world, 2);
    var inside = world with { Position = fate.Position, CurrentFate = 1, Synced = true };
    r.Tick(inside, 3); r.Tick(inside, 4); r.Tick(inside, 5);
    Assert(r.State == RunState.Fighting);
    inside = inside with { InCombat = true, Enemies = [new(99, 1, new(101, 0, 0), 1, false)] };
    r.Tick(inside, 6); return inside;
}

Test("Filters exclude collection, preparation, high level and nearly finished FATEs", () =>
{
    foreach (var invalid in new[] { fate with { IsCombat=false }, fate with { State=EventState.Preparing }, fate with { Level=110 }, fate with { Progress=99 }, fate with { SecondsLeft=20 }, fate with { TerritoryId=10 } })
        Assert(Selection.Choose(world with { Fates=[invalid] }, Options(), new Dictionary<FateKey,double>(), 0) == null);
});
Test("Bonus preference and nearest distance", () =>
{
    var bonus = fate with { Id=2, Bonus=true, Position=new(150,0,0) };
    Assert(Selection.Choose(world with { Fates=[fate,bonus] }, Options(), new Dictionary<FateKey,double>(), 0)?.Id == 2);
    var o=Options(); o.PreferBonus=false;
    Assert(Selection.Choose(world with { Fates=[fate,bonus] }, o, new Dictionary<FateKey,double>(), 0)?.Id == 1);
});
Test("Temporary exclusion expires and does not affect a different instance", () =>
{
    var excluded=new Dictionary<FateKey,double> { [Selection.Key(fate,world)]=300 };
    Assert(Selection.Choose(world,Options(),excluded,299)==null);
    Assert(Selection.Choose(world,Options(),excluded,301)!=null);
    Assert(Selection.Choose(world with { Instance=2 },Options(),excluded,1)!=null);
});
Test("Unknown instances are never guessed", () => Assert(Selection.NextInstance(1,0,new HashSet<int>())==null));
Test("Instance rotation skips visited and current", () =>
{
    Assert(Selection.NextInstance(3,3,new HashSet<int>{3,1})==2);
    Assert(Selection.NextInstance(1,3,new HashSet<int>{1,2,3})==null);
});
Test("Zone cycle wraps and a one-zone route does not teleport to itself", () =>
{
    Assert(Selection.NextZone([zone1,zone2],1001)==zone1);
    Assert(Selection.NextZone([zone1],1000)==null);
});
Test("Death stops all automation", () => { var (r,f)=Started(); r.Tick(world with { Dead=true },1); Assert(r.State==RunState.Faulted && f.Ends==1); });
Test("Logout stops all automation", () => { var (r,f)=Started(); r.Tick(world with { LoggedIn=false },1); Assert(!r.IsRunning && f.Ends==1); });
Test("Duty and PvP stop all automation", () => { var (r,f)=Started(); r.Tick(world with { Restricted=true },1); Assert(!r.IsRunning && f.Ends==1); });
Test("Unexpected zone change stops", () => { var (r,f)=Started(); r.Tick(world with { Territory=1001 },1); Assert(!r.IsRunning && f.Ends==1); });
Test("Partial initialization rolls back", () => { var f=new Fake{FailBegin=true}; var r=new Runner(f); r.Start(Options(),world,0); Assert(r.State==RunState.Faulted && f.Ends==1); });
Test("Settings are copied at session start", () => { var o=Options(); var (r,f)=Started(o:o); o.ExcludedFates.Add(1); r.Tick(world,1); Assert(r.Selected?.Id==1); });
Test("Full successful FATE returns only after confirmed teleport", () =>
{
    var (r,f)=Started(); var inside=Engage(r);
    r.Tick(inside with { Fates=[fate with { Progress=100 }] },7);
    Assert(r.Completed==1);
    r.Tick(inside with { InCombat=false, Fates=[] },8);
    Assert(r.State==RunState.Teleporting && f.Teleports.Single()==home);
    r.Tick(inside with { InCombat=false, Territory=129 },9);
    Assert(r.State==RunState.Teleporting, "Arrival requires a loading transition");
    r.Tick(inside with { Loading=true },10);
    r.Tick(inside with { InCombat=false, Territory=129 },12);
    Assert(r.State==RunState.Finished && f.Confirmations==1 && f.Ends==1);
});
Test("A disappearing FATE does not count as success", () =>
{
    var (r,f)=Started(); var inside=Engage(r); r.Tick(inside with { Fates=[] },7);
    Assert(r.Completed==0 && r.Unconfirmed==1 && f.Teleports.Count==0);
});
Test("A failed FATE does not count as success", () =>
{
    var (r,f)=Started(); var inside=Engage(r); r.Tick(inside with { Fates=[fate with { State=EventState.Failed, Progress=100 }] },7);
    Assert(r.Completed==0 && r.Unconfirmed==1);
});
Test("Success is counted once", () =>
{
    var (r,f)=Started(); var inside=Engage(r) with { InCombat=false, Fates=[fate with { Progress=100 }] };
    r.Tick(inside,7); r.Tick(inside,8); r.Tick(inside,9); Assert(r.Completed==1);
});
Test("No movement after Stop", () => { var (r,f)=Started(); r.Tick(world,1); r.Stop(); r.Tick(world,2); Assert(f.Moves==0); });
Test("Stuck navigation excludes the selected FATE", () =>
{
    var (r,f)=Started(); r.Tick(world,1); r.Tick(world,2); r.Tick(world with { NavBusy=true },30);
    Assert(r.State==RunState.Selecting); r.Tick(world,31); Assert(r.Selected==null);
});
Test("Combat integration exception stops and releases session", () =>
{
    var (r,f)=Started(); var inside=Engage(r); f.FailCombat=true; r.Tick(inside,7);
    Assert(r.State==RunState.Faulted && f.Ends==1);
});
Test("Idle zone cycle uses next configured zone", () =>
{
    var o=Options(); o.RotateInstances=false; o.Route=[zone1,zone2]; var idle=world with { Fates=[] };
    var (r,f)=Started(idle,o); r.Tick(idle,1); r.Tick(idle,12);
    Assert(r.State==RunState.Teleporting && f.Teleports.Single()==zone2);
});
Test("Instance change approaches hub and waits for observed result", () =>
{
    var o=Options(); o.Route=[zone1,zone2]; var idle=world with { Fates=[] };
    var (r,f)=Started(idle,o); r.Tick(idle,1); r.Tick(idle,12); Assert(f.Teleports.Single()==zone1);
    r.Tick(idle with { Loading=true },13); r.Tick(idle,15); Assert(r.State==RunState.HubApproach);
    r.Tick(idle with { CanChangeInstance=true },16); Assert(f.Instances.Single()==2);
    r.Tick(idle with { TravelBusy=true, Instance=2 },17); Assert(r.State==RunState.InstanceChange);
    r.Tick(idle with { Instance=2 },18); Assert(r.State==RunState.Selecting);
});
Test("Cancelled teleport has a deadline", () =>
{
    var o=Options(); o.Route=[zone2]; var (r,f)=Started(o:o); r.Tick(world,1); r.Tick(world,92);
    Assert(r.State==RunState.Faulted && f.Ends==1);
});
Test("Duplicate route territories are rejected", () => { var o=Options(); o.Route=[zone1,zone1]; Throws(o.Validate); });
Test("Positive quota requires a return destination", () => Throws(() => new RunOptions().Validate()));
Test("No quota allows no return destination", () => new RunOptions{ReturnAfterFates=0}.Validate());
Test("Free destinations never consume a ticket", () => Assert(TravelPolicy.Choose(new(){UseTickets=true},0,10,0,0)==Payment.Free));
Test("Tickets are used even without enough gil", () => Assert(TravelPolicy.Choose(new(){UseTickets=true},1000,1,0,0)==Payment.Ticket));
Test("Ticket switch off uses gil", () => Assert(TravelPolicy.Choose(new(){UseTickets=false},1000,10,2000,0)==Payment.Gil));
Test("No ticket stops before spending unless fallback was chosen", () =>
{
    Throws(() => TravelPolicy.Choose(new(){UseTickets=true},1000,0,2000,0));
    Assert(TravelPolicy.Choose(new(){UseTickets=true,PayGilWhenTicketsRunOut=true},1000,0,2000,0)==Payment.Gil);
});
Test("Ticket threshold is inclusive and low fares use gil", () =>
{
    var o=new RunOptions{UseTickets=true,TicketMinimumFare=500};
    Assert(TravelPolicy.Choose(o,500,1,1000,0)==Payment.Ticket);
    Assert(TravelPolicy.Choose(o,499,0,1000,0)==Payment.Gil);
});
Test("Budget accounts for the next trip including return", () =>
{
    var o=new RunOptions{MaximumTeleportGil=1000};
    Assert(TravelPolicy.Choose(o,400,0,10000,600)==Payment.Gil);
    Throws(() => TravelPolicy.Choose(o,401,0,10000,600));
});
Test("Not enough gil refuses the request", () => Throws(() => TravelPolicy.Choose(new(),500,0,499,0)));
Test("Job change stops and releases ownership", () => { var (r,f)=Started(); r.Tick(world with { Job=23 },1); Assert(r.State==RunState.Faulted && f.Ends==1); });
Test("Unexpected loading stops before movement", () => { var (r,f)=Started(); r.Tick(world with { Loading=true },1); Assert(r.State==RunState.Faulted && f.Moves==0); });
Test("An unexpected dialog stops the session", () => { var (r,f)=Started(); r.Tick(world with { Occupied=true },1); Assert(!r.IsRunning && f.Ends==1); });
Test("Other travel automation interrupts the session", () => { var (r,f)=Started(); r.Tick(world with { TravelBusy=true },1); Assert(r.State==RunState.Faulted && f.Ends==1); });

if (args.Length > 0)
{
    Test("Every language has all keys and matching format placeholders", () =>
    {
        var folder=args[0];
        var source=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(folder,"pt-BR.json")))!;
        Assert(source.Count>140);
        foreach(var language in new[]{"en","ja","de","fr","es","pt-BR"})
        {
            var translated=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(folder,language+".json")))!;
            Assert(source.Keys.ToHashSet().SetEquals(translated.Keys), language+" keys differ");
            foreach(var (key,value) in translated)
            {
                string[] Holes(string t) => Regex.Matches(t,@"\{\d+\}").Select(m=>m.Value).Order().ToArray();
                Assert(Holes(key).SequenceEqual(Holes(value)), language+": "+key);
                _=string.Format(value,Enumerable.Range(0,10).Select(i=>(object)i).ToArray());
            }
        }
    });
}
if (args.Length > 2)
{
    Test("Source privacy guard and click-only donation link", () => PrivacyAudit.Source(args[1]));
    Test("Compiled assembly capabilities and bundled assets", () => PrivacyAudit.Binary(args[2]));
    Test("Every literal UI and status key has a translation", () => PrivacyAudit.LocalizationCoverage(args[1]));
}
var failures=0;
foreach(var (name,run) in tests)
{
    try { run(); Console.WriteLine("PASS "+name); }
    catch(Exception ex) { failures++; Console.WriteLine("FAIL "+name+": "+ex.Message); }
}
Console.WriteLine($"{tests.Count-failures}/{tests.Count} tests passed");
return failures==0?0:1;

sealed class Fake : IRuntime
{
    public int Ends, Moves, Confirmations;
    public bool FailBegin, FailCombat;
    public readonly List<Destination> Teleports=[];
    public readonly List<int> Instances=[];
    public void Begin(RunOptions o) { if(FailBegin) throw new InvalidOperationException("partial setup failure"); }
    public void End()=>Ends++;
    public void Move(Vector3 p,bool fly)=>Moves++;
    public void StopMovement() { }
    public void Mount() { }
    public void Dismount() { }
    public void Sync() { }
    public void SetCombat(bool enabled) { if(enabled&&FailCombat) throw new InvalidOperationException("IPC gone"); }
    public void Target(ulong id) { }
    public void Teleport(Destination d)=>Teleports.Add(d);
    public void ConfirmTeleport()=>Confirmations++;
    public void ChangeInstance(int instance)=>Instances.Add(instance);
    public void Log(string message) { }
}
