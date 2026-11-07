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
Test("Starting requires fresh confirmation and cancellation prevents a delayed start", () =>
{
    var gate = new StartConfirmation();
    Assert(!gate.Confirm(false));
    Assert(gate.Request(false) && gate.Pending);
    gate.Cancel();
    Assert(!gate.Confirm(false) && !gate.Pending);
    Assert(gate.Request(false) && gate.Confirm(false));
    Assert(!gate.Confirm(false));
    Assert(!gate.Request(true) && !gate.Pending);
    Assert(gate.Request(false));
    Assert(!gate.Confirm(true) && !gate.Pending);
    Assert(gate.Request(false) && gate.Confirm(false));
});
RunOptions Options() => new() { ReturnDestination = home, ReturnAfterFates = 1, UseMount = false, SummonChocobo = false, IdleSeconds = 10, AutoRetry = false };
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

Test("Filters exclude unknown types, unstartable preparation, high level and nearly finished FATEs", () =>
{
    foreach (var invalid in new[] { fate with { IsCombat=false }, fate with { State=EventState.Preparing }, fate with { Level=110 }, fate with { Progress=99 }, fate with { SecondsLeft=20 }, fate with { TerritoryId=10 } })
        Assert(Selection.Choose(world with { Fates=[invalid] }, Options(), new Dictionary<FateKey,double>(), 0) == null);
});
Test("Client rule values separate combat, collection, escort and defense", () =>
{
    Assert(FateRules.FromRule(1)==FateKind.Combat);
    Assert(FateRules.FromRule(2)==FateKind.Collection);
    Assert(FateRules.FromRule(3)==FateKind.Escort);
    Assert(FateRules.FromRule(4)==FateKind.Defense);
    Assert(FateRules.FromRule(0)==FateKind.Unknown && FateRules.FromRule(7)==FateKind.Unknown);
});
Test("Collection and escort can be enabled or excluded independently", () =>
{
    var o=Options();
    foreach(var kind in new[]{FateKind.Collection,FateKind.Escort,FateKind.Defense})
        Assert(Selection.Choose(world with{Fates=[fate with{IsCombat=false,Kind=kind}]},o,new Dictionary<FateKey,double>(),0)!=null);
    o.IncludeCollection=false;
    Assert(Selection.Choose(world with{Fates=[fate with{Kind=FateKind.Collection}]},o,new Dictionary<FateKey,double>(),0)==null);
});
Test("NPC activation survives the server starting its timer", () =>
{
    var pending=fate with{State=EventState.Preparing,CanStart=true,SecondsLeft=0,StartedAt=0,Position=Vector3.Zero};
    var npc=new ObjectiveActor(501,1,ObjectiveRole.Starter,Vector3.Zero);
    var w=world with{Fates=[pending],Objectives=[npc],CurrentFate=1,Synced=true};
    var (r,rt)=Started(w);
    for(int i=1;i<=5;i++) r.Tick(w,i);
    Assert(r.State==RunState.StartingEvent && rt.Interactions.Count>0);
    r.Tick(w with{Fates=[pending with{State=EventState.Running,StartedAt=99,SecondsLeft=600}]},6);
    Assert(r.State==RunState.Syncing && r.Selected?.StartedAt==99);
});
Test("Collection delivers only to its own recipient and confirms progress", () =>
{
    var collect=fate with{Kind=FateKind.Collection,HeldItems=15,Position=Vector3.Zero};
    var recipient=new ObjectiveActor(502,1,ObjectiveRole.Recipient,Vector3.Zero);
    var w=world with{Fates=[collect],CurrentFate=1,Synced=true,Objectives=[recipient with{Id=999,FateId=2},recipient]};
    var (r,rt)=Started(w);
    for(int i=1;i<=6;i++) r.Tick(w,i);
    Assert(rt.Interactions.Count>0 && rt.Interactions.All(a=>a.Id==502));
    r.Tick(w with{Fates=[collect with{HeldItems=0,HandedIn=3}]},7);
    r.Tick(w with{Fates=[collect with{State=EventState.Ended,Progress=100,HandedIn=3}]},8);
    Assert(r.Completed==1);
});
Test("Collection periodically delivers a partial batch without assuming completion", () =>
{
    var collect=fate with{Kind=FateKind.Collection,HeldItems=4,Position=Vector3.Zero};
    var w=world with{Fates=[collect],CurrentFate=1,Synced=true,Objectives=[new(502,1,ObjectiveRole.Recipient,Vector3.Zero)]};
    var (r,rt)=Started(w);
    for(int i=1;i<=6;i++)r.Tick(w,i);
    Assert(rt.Interactions.Count==0);
    r.Tick(w,47);
    Assert(rt.Interactions.Count==1 && r.Completed==0);
    r.Tick(w with{Fates=[collect with{HeldItems=0,HandedIn=4,Progress=20}]},48);
    Assert(r.IsRunning && r.Completed==0);
});
Test("Collection stops to fight an attacker instead of interacting", () =>
{
    var collect=fate with{Kind=FateKind.Collection,HeldItems=3,Position=Vector3.Zero};
    var w=world with{Fates=[collect],CurrentFate=1,Synced=true,Objectives=[new(502,1,ObjectiveRole.Recipient,Vector3.Zero)]};
    var (r,rt)=Started(w);
    for(int i=1;i<=4;i++) r.Tick(w,i);
    r.Tick(w with{InCombat=true,Enemies=[new(99,1,Vector3.Zero,1,true)]},5);
    r.Tick(w with{InCombat=true,Enemies=[new(99,1,Vector3.Zero,1,true)]},6);
    Assert(rt.Interactions.Count==0 && rt.LastTarget==99 && rt.CombatEnabled);
});
Test("Collection repeats full batches until the observed event finishes", () =>
{
    var collect=fate with{Kind=FateKind.Collection,HeldItems=15,Position=Vector3.Zero};
    var recipient=new ObjectiveActor(502,1,ObjectiveRole.Recipient,Vector3.Zero);
    var pickup=new ObjectiveActor(504,1,ObjectiveRole.Pickup,Vector3.Zero);
    var w=world with{Fates=[collect],CurrentFate=1,Synced=true,Objectives=[recipient,pickup]};
    var (r,rt)=Started(w);
    for(int i=1;i<=6;i++)r.Tick(w,i);
    r.Tick(w with{Fates=[collect with{HeldItems=0,HandedIn=15,Progress=20}]},10);
    Assert(rt.Interactions.Last().Role==ObjectiveRole.Pickup && r.Completed==0);
    r.Tick(w with{Fates=[collect with{HeldItems=15,HandedIn=15,Progress=20}]},14);
    Assert(rt.Interactions.Last().Role==ObjectiveRole.Recipient && r.Completed==0);
    r.Tick(w with{Fates=[collect with{HeldItems=0,HandedIn=30,Progress=45}]},18);
    Assert(r.IsRunning && r.Completed==0);
});
Test("Escort follows its objective without the combat-only idle timeout", () =>
{
    var escort=fate with{Kind=FateKind.Escort,Position=Vector3.Zero};
    var w=world with{Fates=[escort],CurrentFate=1,Synced=true,Objectives=[new(503,1,ObjectiveRole.Escort,new(20,0,0))]};
    var (r,rt)=Started(w);
    for(int i=1;i<=6;i++) r.Tick(w,i);
    Assert(rt.LastMove==new Vector3(20,0,0));
    r.Tick(w with{Position=new(18,0,0)},70);
    Assert(r.State==RunState.Fighting && !rt.CombatEnabled);
});
Test("Owned dialogue advances while unrelated dialogue still stops", () =>
{
    var (r,rt)=Started(); r.Tick(world,1);
    r.Tick(world with{Occupied=true,OwnEventInteraction=true},2);
    Assert(r.IsRunning && rt.Advances==1);
    r.Tick(world with{Occupied=true},3);
    Assert(!r.IsRunning);
});
Test("An issued interaction waits for event context without clicking or stopping", () =>
{
    var (r,rt)=Started(); r.Tick(world,1);
    r.Tick(world with{Occupied=true,EventInteractionPending=true},2);
    Assert(r.IsRunning && rt.Advances==0 && !rt.CombatEnabled);
    r.Tick(world with{Occupied=true,OwnEventInteraction=true},3);
    Assert(r.IsRunning && rt.Advances==1);
});
Test("Own dismount transition can be occupied briefly without adopting a dialog", () =>
{
    var w=world with{CurrentFate=1,Position=fate.Position,Mounted=true,Synced=true};
    var (r,rt)=Started(w);
    r.Tick(w,1);r.Tick(w,2);r.Tick(w,3);r.Tick(w,4);
    Assert(r.State==RunState.Landing);
    r.Tick(w with{Mounted=false,Occupied=true},4.25);
    Assert(r.IsRunning && rt.Advances==0);
    r.Tick(w with{Mounted=false,Casting=true},4.5);
    Assert(r.State==RunState.Landing);
    r.Tick(w with{Mounted=false},5);
    Assert(r.State==RunState.Syncing);
    r.Tick(w with{Mounted=false,Occupied=true,DialogueOpen=true},5.25);
    Assert(!r.IsRunning);
});
Test("Occupied after our mount transition deadline still stops", () =>
{
    var w=world with{CurrentFate=1,Position=fate.Position,Mounted=true,Synced=true};
    var (r,rt)=Started(w);
    for(int i=1;i<=4;i++)r.Tick(w,i);
    r.Tick(w with{Occupied=true},10);
    Assert(!r.IsRunning && rt.Advances==0);
});
Test("Interaction ownership waits for context then accepts the expected actor and handler", () =>
{
    var lease=new InteractionLease(); lease.Begin(502,[0x801A0001,0x000D0007],0);
    Assert(lease.Observe(.25,true,default,default,false)==InteractionStatus.Waiting);
    Assert(lease.Observe(1,true,new(502,0x000D0007),default,false)==InteractionStatus.Owned);
    Assert(lease.Observe(2,false,default,default,false)==InteractionStatus.None && !lease.Active);
});
Test("Interaction ownership rejects wrong NPCs and unrelated handlers", () =>
{
    foreach(var signal in new[]{new EventSignal(999,0x801A0001),new EventSignal(502,0x00040001)})
    {
        var lease=new InteractionLease();lease.Begin(502,[0x801A0001],0);
        Assert(lease.Observe(3,true,signal,default,false)==InteractionStatus.Foreign);
        Assert(!lease.Active);
    }
});
Test("Scene actor can identify an owned dialog while the event object is unset", () =>
{
    var lease=new InteractionLease();lease.Begin(502,[0x801A0001],0);
    Assert(lease.Observe(1,true,new(0,0x801A0001),default,false,502)==InteractionStatus.Owned);
    lease.Begin(502,[0x801A0001],0);
    Assert(lease.Observe(3,true,new(0,0x00040001),default,false,502)==InteractionStatus.Foreign);
});
Test("NPC selector can be owned before scene fields exist but ownership expires", () =>
{
    var lease=new InteractionLease();lease.Begin(502,[0x801A0001],0);
    Assert(lease.Observe(.25,true,default,default,true)==InteractionStatus.Owned);
    Assert(lease.Observe(15,true,default,default,true)==InteractionStatus.Owned);
    Assert(lease.Observe(InteractionLease.MaxSeconds,true,default,default,true)==InteractionStatus.Expired && !lease.Active);
});
Test("An interaction that opens nothing releases its retry window", () =>
{
    var lease=new InteractionLease();lease.Begin(502,[0x801A0001],0);
    Assert(lease.Observe(.5,false,default,default,false)==InteractionStatus.Waiting);
    Assert(lease.Observe(3,false,default,default,false)==InteractionStatus.None && !lease.Active);
});
Test("Gemstone session counter counts observed gains, not the initial balance", () =>
{
    var (r,rt)=Started(world with{Gemstones=100});
    r.Tick(world with{Gemstones=116},1); r.Tick(world with{Gemstones=116},2);
    r.Tick(world with{Gemstones=110},3); r.Tick(world with{Gemstones=126},4);
    Assert(r.GemstonesEarned==32);
});
Test("Live NPC speaker comparison handles localization and refuses incomplete labels", () =>
{
    Assert(DialogueSpeaker.Matches("pelupelu peddler", "Pelupelu Peddler"));
    Assert(DialogueSpeaker.Matches("  Pelupelu  Peddler", "Pelupelu\u00a0Peddler "));
    Assert(DialogueSpeaker.Matches("Marchand égaré", "Marchand e\u0301gare\u0301"));
    Assert(DialogueSpeaker.Matches("ペルペル族の商人", "ペルペル族の商人"));
    foreach(var label in new string?[]{null,"", " ", "Pelupelu", "Other Peddler", "Pelupelu Peddler's assistant", new('a',257)})
        Assert(!DialogueSpeaker.Matches("Pelupelu Peddler", label));
    Assert(!DialogueSpeaker.Matches(null,"Pelupelu Peddler"));
});
Test("Five live-speaker pages and their Yes confirmation lead to sync and combat", () =>
{
    var pending=fate with{State=EventState.Preparing,CanStart=true,SecondsLeft=0,StartedAt=0,Position=Vector3.Zero};
    var w=world with{Fates=[pending],Objectives=[new(501,1,ObjectiveRole.Starter,Vector3.Zero)],CurrentFate=1};
    var (r,rt)=Started(w); for(var i=1;i<=5;i++) r.Tick(w,i);
    var guard=new LocalDialogueGuard(); var lease=new InteractionLease(); lease.Begin(501,[0x801A0001],5);
    for(var page=6;page<=10;page++)
    {
        var local=guard.Observe(true,true,true,DialogueSpeaker.Matches("pelupelu peddler","Pelupelu Peddler"),false,700,page);
        var status=lease.Observe(page,true,default,default,false,0,local);
        Assert(status==InteractionStatus.Owned);
        r.Tick(w with{OwnEventInteraction=status==InteractionStatus.Owned,Occupied=true,DialogueOpen=true},page);
    }
    var yes=guard.Observe(true,true,false,false,true,700,11);
    Assert(yes && lease.Observe(11,true,default,default,false,0,yes)==InteractionStatus.Owned);
    r.Tick(w with{OwnEventInteraction=yes,Occupied=true,DialogueOpen=true},11);
    Assert(rt.Advances==6 && r.IsRunning);
    Assert(lease.Observe(12,false,default,default,false)==InteractionStatus.None);
    var running=w with{Fates=[pending with{State=EventState.Running,SecondsLeft=600,StartedAt=99}]};
    r.Tick(running,12); r.Tick(running,13);
    Assert(rt.Syncs==1 && r.State==RunState.Syncing);
    r.Tick(running with{Synced=true},14);
    r.Tick(running with{Synced=true,Enemies=[new(99,1,Vector3.One,1,false)]},15);
    Assert(r.State==RunState.Fighting && rt.LastTarget==99 && rt.CombatEnabled);
});
Test("Local conversation advances several pages and only its own confirmation", () =>
{
    var guard=new LocalDialogueGuard(); var lease=new InteractionLease(); lease.Begin(502,[0x801A0001],0);
    for(var page=1;page<=5;page++)
    {
        var matched=guard.Observe(true,true,true,true,false,700,page);
        Assert(matched && lease.Observe(page,true,default,default,false,0,matched)==InteractionStatus.Owned);
    }
    Assert(!guard.Observe(true,true,false,false,false,0,5.25));
    Assert(guard.Observe(true,true,false,false,true,700,6));
    Assert(guard.Observe(true,true,false,false,true,701,6), "The FATE start Yes/No has its own callback");
    Assert(!guard.Observe(true,true,false,false,true,700,11), "A prompt long after the conversation is not ours");
});
Test("Local conversation never overrides another actor, speaker or populated event", () =>
{
    var guard=new LocalDialogueGuard();
    Assert(!guard.Observe(true,true,false,false,true,700,1));
    Assert(!guard.Observe(true,true,true,false,false,700,1));
    Assert(!guard.Observe(true,false,true,true,false,700,1));
    Assert(!guard.Observe(false,true,true,true,false,700,1));
    Assert(!guard.Observe(true,true,true,true,false,0,1));
    var lease=new InteractionLease(); lease.Begin(502,[0x801A0001],0);
    Assert(lease.Observe(3,true,new(999,0x00040001),default,false,0,true)==InteractionStatus.Foreign);
    lease.Begin(502,[0x801A0001],0);
    Assert(lease.Observe(1,true,new(0xE0000000,0),default,false,0xE0000000,true)==InteractionStatus.Owned);
    Assert(lease.Observe(InteractionLease.MaxSeconds,true,default,default,false,0,true)==InteractionStatus.Expired);
});
Test("Long verified conversation keeps ownership across narration and other speakers", () =>
{
    var guard=new LocalDialogueGuard(); var lease=new InteractionLease(); lease.Begin(502,[0x801A0001],0);
    Assert(guard.Observe(true,true,true,true,false,700,1));
    // 12 pages at ~1.2 s each: speaker header empty/different, same native callback.
    for(var i=1;i<=12;i++)
    {
        var t=1+i*1.2;
        var ok=guard.Observe(true,true,true,false,false,700,t);
        Assert(ok && lease.Observe(t,true,default,default,false,0,ok)==InteractionStatus.Owned);
    }
    Assert(guard.Observe(true,true,false,false,true,700,17));
    // A different callback, or a gap, is never adopted.
    Assert(!guard.Observe(true,true,true,false,false,701,17.5));
    var fresh=new LocalDialogueGuard();
    Assert(fresh.Observe(true,true,true,true,false,700,1));
    Assert(!fresh.Observe(true,true,true,false,false,700,7));
});
Test("Conversation page transitions wait briefly without losing ownership", () =>
{
    var lease=new InteractionLease(); lease.Begin(502,[0x801A0001],0);
    Assert(lease.Observe(1,true,default,default,false,0,true)==InteractionStatus.Owned);
    Assert(lease.Observe(1.25,false,default,default,false)==InteractionStatus.Waiting);
    Assert(lease.Observe(1.5,true,default,default,false,0,true)==InteractionStatus.Owned);
    Assert(lease.Observe(2.5,false,default,default,false)==InteractionStatus.None);
});
Test("NPC conversation completion proceeds through sync to combat", () =>
{
    var pending=fate with{State=EventState.Preparing,CanStart=true,SecondsLeft=0,StartedAt=0,Position=Vector3.Zero};
    var w=world with{Fates=[pending],Objectives=[new(501,1,ObjectiveRole.Starter,Vector3.Zero)],CurrentFate=1};
    var (r,rt)=Started(w); for(var i=1;i<=5;i++) r.Tick(w,i);
    for(var i=6;i<=10;i++) r.Tick(w with{OwnEventInteraction=true,Occupied=true,DialogueOpen=true},i);
    Assert(rt.Advances==5 && r.IsRunning);
    var running=w with{Fates=[pending with{State=EventState.Running,SecondsLeft=600,StartedAt=99}]};
    r.Tick(running,11); r.Tick(running,12); Assert(rt.Syncs==1 && r.State==RunState.Syncing);
    r.Tick(running with{Synced=true},13);
    r.Tick(running with{Synced=true,Enemies=[new(99,1,Vector3.One,1,false)]},14);
    Assert(r.State==RunState.Fighting && rt.LastTarget==99 && rt.CombatEnabled);
});
Test("Daivadipa and other known group bosses are excluded by default", () =>
{
    foreach(var id in new ushort[]{196,506,1106,1432,1464,1763,1855,1871,1922})
        Assert(Selection.Choose(world with{Fates=[fate with{Id=id}]},Options(),new Dictionary<FateKey,double>(),0)==null);
    var o=Options();o.ExcludeGroupBosses=false;
    Assert(Selection.Choose(world with{Fates=[fate with{Id=1763}]},o,new Dictionary<FateKey,double>(),0)!=null);
});
Test("Collection can deliver its remaining items during the ending phase", () =>
{
    var collect=fate with{Kind=FateKind.Collection,HeldItems=3,Position=Vector3.Zero};
    var w=world with{Fates=[collect],CurrentFate=1,Synced=true,Objectives=[new(502,1,ObjectiveRole.Recipient,Vector3.Zero)]};
    var (r,rt)=Started(w);
    for(int i=1;i<=5;i++)r.Tick(w,i);
    r.Tick(w with{CurrentFate=0,Fates=[collect with{State=EventState.Ending,Progress=100}]},6);
    Assert(r.IsRunning && rt.Interactions.Count==1 && r.Completed==0);
    r.Tick(w with{Fates=[collect with{State=EventState.Ending,Progress=100,HeldItems=0,HandedIn=3}]},7);
    Assert(r.Completed==1);
});
Test("Nearest FATE wins and bonus only breaks equal-distance ties", () =>
{
    var bonus = fate with { Id=2, Bonus=true, Position=new(150,0,0) };
    Assert(Selection.Choose(world with { Fates=[fate,bonus] }, Options(), new Dictionary<FateKey,double>(), 0)?.Id == 1);
    Assert(Selection.Choose(world with { Fates=[fate,bonus with{Position=fate.Position}] }, Options(), new Dictionary<FateKey,double>(), 0)?.Id == 2);
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
Test("Zone without aetheryte is reached through Idyllshire and its aethernet gate", () =>
{
    var hinterlands=new Destination(399,75,"The Dravanian Hinterlands — via Idyllshire");
    var o=Options(); o.RotateInstances=false; o.Route=[zone1,hinterlands]; var idle=world with { Fates=[] };
    var (r,f)=Started(idle,o); r.Tick(idle,1); r.Tick(idle,12);
    Assert(r.State==RunState.Teleporting && f.Teleports.Single()==hinterlands);
    r.Tick(idle with { Loading=true },13);
    r.Tick(idle with { Territory=478 },15);
    Assert(r.State==RunState.Gateway, "Landing in Idyllshire continues through the aethernet");
    for(var t=16;t<=30 && f.Shards.Count==0;t++) r.Tick(idle with { Territory=478 },t);
    Assert(f.Shards.Single()==91);
    r.Tick(idle with { Territory=478, TravelBusy=true },31);
    r.Tick(idle with { Territory=399, Loading=true },32);
    r.Tick(idle with { Territory=399, TravelBusy=true },33);
    Assert(r.State==RunState.Gateway, "Waits for the travel module to finish");
    r.Tick(idle with { Territory=399 },34);
    Assert(r.State==RunState.Selecting && r.IsRunning);
    r.Tick(idle with { Territory=399 },35);
    Assert(r.IsRunning, "The zone after the gateway is the expected territory");
});
Test("Gateway that never leaves the city stops with a clear reason", () =>
{
    var hinterlands=new Destination(399,75,"Hinterlands");
    var o=Options(); o.RotateInstances=false; o.Route=[zone1,hinterlands]; var idle=world with { Fates=[] };
    var (r,f)=Started(idle,o); r.Tick(idle,1); r.Tick(idle,12);
    r.Tick(idle with { Loading=true },13); r.Tick(idle with { Territory=478 },15);
    for(var t=16;t<=80;t+=4) r.Tick(idle with { Territory=478 },t);
    Assert(!r.IsRunning && f.Shards.Count is >= 1 and <= 3);
});
Test("Pause releases control, freezes the session clock and resume continues with the same counters", () =>
{
    var (r,f)=Started(); r.Tick(world,1); Assert(r.Selected!=null);
    r.RequestPause(); r.Tick(world,2);
    Assert(r.IsPaused && r.IsRunning && r.Selected==null && f.Suspends==1);
    var frozen=r.ElapsedSeconds;
    r.Tick(world with { Territory=1001, Job=19 },100);
    Assert(r.IsPaused && r.ElapsedSeconds==frozen, "Zone or job changes while paused do not stop the session");
    r.Resume(world with { InCombat=true },101); Assert(r.IsPaused, "Resume waits for out of combat");
    r.Resume(world with { Territory=1001, Job=19 },102);
    Assert(r.State==RunState.Selecting && f.Resumes==1);
    r.Tick(world with { Territory=1001, Job=19 },103);
    Assert(r.IsRunning, "The new zone and job are adopted on resume");
    Assert(r.ElapsedSeconds < 10, "Paused time is not counted");
});
Test("Pause after this FATE waits for the FATE to end", () =>
{
    var o=Options(); o.ReturnAfterFates=0; o.ReturnDestination=null;
    var (r,f)=Started(o:o); var inside=Engage(r);
    r.TogglePauseAfterFate(); Assert(r.PauseAfterFate);
    r.Tick(inside,7); Assert(!r.IsPaused, "Keeps fighting");
    r.Tick(inside with { Fates=[fate with { Progress=100 }] },8);
    r.Tick(inside with { InCombat=false, Fates=[] },9);
    Assert(r.IsPaused && r.Completed==1 && f.Suspends==1);
});
Test("Bag space and durability limits request a return", () =>
{
    var o=Options(); o.ReturnAfterFates=0; o.ReturnWhenBagSlotsBelow=5;
    var (r,_)=Started(world,o); r.Tick(world with { FreeBagSlots=3 },1);
    Assert(r.PendingReturnGoal==ReturnGoal.BagFull);
    o=Options(); o.ReturnAfterFates=0; o.ReturnWhenDurabilityBelow=20;
    (r,_)=Started(world,o); r.Tick(world with { MinDurability=10 },1);
    Assert(r.PendingReturnGoal==ReturnGoal.Durability);
    Assert(SessionGoals.Evaluate(o,0,0,0,0,0,-1,-1)==ReturnGoal.None, "Unknown values never trigger");
});
Test("Self-repair runs between FATEs and is confirmed by durability", () =>
{
    var o=Options(); o.ReturnAfterFates=0; o.ReturnWhenDurabilityBelow=20; o.SelfRepair=true; o.RepairBelowPercent=30;
    var worn=world with { Fates=[], MinDurability=10, CanSelfRepair=true };
    var (r,f)=Started(worn,o); r.Tick(worn,1);
    Assert(r.State==RunState.Repairing && f.RepairStarts==1 && r.PendingReturnGoal==ReturnGoal.None, "Repair replaces the durability return");
    r.Tick(worn with { RepairBusy=true },2);
    r.Tick(worn with { MinDurability=100 },3);
    Assert(r.State==RunState.Selecting && r.Repairs==1);
});
Test("Failed repair disables it and the durability limit then returns", () =>
{
    var o=Options(); o.ReturnAfterFates=0; o.ReturnWhenDurabilityBelow=20; o.SelfRepair=true;
    var worn=world with { Fates=[], MinDurability=10, CanSelfRepair=true };
    var (r,f)=Started(worn,o); r.Tick(worn,1); r.Tick(worn,2);
    Assert(r.Repairs==0 && r.State==RunState.Selecting);
    r.Tick(worn,3);
    Assert(f.RepairStarts==1 && r.PendingReturnGoal==ReturnGoal.Durability);
});
Test("Food is eaten when Well Fed is low and confirmed by the new timer", () =>
{
    var o=Options(); o.UseFood=true; o.FoodItemId=4650; o.FoodRefreshMinutes=5;
    var hungry=world with { Fates=[], FoodSecondsLeft=60, FoodCount=3, CanUseFood=true };
    var (r,f)=Started(hungry,o); r.Tick(hungry,1);
    Assert(f.FoodUses==1);
    r.Tick(hungry with { FoodSecondsLeft=1800 },2);
    Assert(r.FoodUsed==1);
    r.Tick(hungry with { FoodSecondsLeft=1800 },70); Assert(f.FoodUses==1, "Not eaten again while fed");
});
Test("Job queue equips the next gearset when the target level is reached", () =>
{
    var o=Options(); o.ReturnAfterFates=0; o.UseJobQueue=true;
    o.JobQueue=[new JobGoal{Gearset=3,Job=22,Name="Dragoon",TargetLevel=90}, new JobGoal{Gearset=5,Job=19,Name="Paladin",TargetLevel=90}];
    var idle=world with { Fates=[] };
    var (r,f)=Started(idle,o); r.Tick(idle,1);
    Assert(r.State==RunState.SwitchingJob && f.Gearsets.Single()==5 && f.Suspends==1);
    r.Tick(idle,2); Assert(r.IsRunning, "Waiting for the job change is not an unexpected change");
    r.Tick(idle with { Job=19, Level=80 },3);
    Assert(r.State==RunState.Selecting && f.Resumes==1 && r.JobSwitches==1);
    r.Tick(idle with { Job=19, Level=90 },20);
    Assert(r.PendingReturnGoal==ReturnGoal.JobQueueDone);
});
Test("Gemstone balance return goes to the chosen trader city; other goals use the return city", () =>
{
    var tuliyollal=new Destination(1185,216,"Tuliyollal");
    var o=Options(); o.ReturnAfterFates=0; o.AvoidGemstoneCap=true; o.GemstoneCapReserve=100; o.GemstoneDestination=tuliyollal;
    var rich=world with { Fates=[], Gemstones=1450, GemstoneCap=1500 };
    var (r,f)=Started(world with { Gemstones=1000, GemstoneCap=1500 },o); r.Tick(rich,1);
    Assert(r.PendingReturnGoal==ReturnGoal.CurrencyCap && f.Teleports.Single()==tuliyollal);
    o=Options(); o.GemstoneDestination=tuliyollal;
    (r,f)=Started(o:o); var inside=Engage(r);
    r.Tick(inside with { Fates=[fate with { Progress=100 }] },7); r.Tick(inside with { InCombat=false, Fates=[] },8);
    Assert(r.PendingReturnGoal==ReturnGoal.Fates && f.Teleports.Single()==home);
    Assert(GemstoneTraders.Has(tuliyollal) && !GemstoneTraders.Has(home));
});
Test("FATE start Yes/No is accepted right after our conversation or interaction, never otherwise", () =>
{
    var guard=new LocalDialogueGuard();
    Assert(guard.Observe(true,true,true,true,false,700,1));
    Assert(guard.Observe(true,true,false,false,true,950,3), "Different callback right after the verified pages");
    var direct=new LocalDialogueGuard();
    Assert(direct.Observe(true,true,false,false,true,950,1,recentInteraction:true), "Prompt opened directly by our interaction");
    var player=new LocalDialogueGuard();
    Assert(!player.Observe(true,true,false,false,true,950,1), "A prompt with no conversation or interaction of ours");
    Assert(!new LocalDialogueGuard().Observe(true,false,false,false,true,950,1,recentInteraction:true), "Never when the FATE NPC is not the bound target");
    Assert(!new LocalDialogueGuard().Observe(false,true,false,false,true,950,1,recentInteraction:true), "Never during another native event");
});
Test("Occupied hand-over between the last page and the Yes/No keeps ownership briefly", () =>
{
    var lease=new InteractionLease(); lease.Begin(502,[0x801A0001],0);
    Assert(lease.Observe(1,true,default,default,false,0,true)==InteractionStatus.Owned);
    Assert(lease.Observe(1.8,true,default,default,false,0,false)==InteractionStatus.Waiting, "Occupied, no window yet");
    Assert(lease.Observe(2.2,true,default,default,false,0,true)==InteractionStatus.Owned, "Yes/No recognised");
    Assert(lease.Observe(4,true,default,default,false,0,false)==InteractionStatus.Foreign, "A longer unknown dialog is still refused");
});
Test("A prompt the player opens (discard item) is waited out, never clicked, and does not stop the session", () =>
{
    var (r,f)=Started(); var inside=Engage(r);
    var advances=f.Advances;
    r.Tick(inside with { DialogueOpen=true },7); r.Tick(inside with { DialogueOpen=true },30);
    Assert(r.IsRunning && f.Advances==advances, "No clicks on a dialog the plugin did not open");
    r.Tick(inside,31); Assert(r.IsRunning && r.State==RunState.Fighting);
    r.Tick(inside with { DialogueOpen=true, Occupied=true },32);
    Assert(!r.IsRunning, "An unrelated NPC event still stops the session");
});
Test("A prioritized FATE (from Achievements) is chosen before a nearer one", () =>
{
    var near=fate with { Id=1, Position=new(10,0,0) }; var far=fate with { Id=2, Name="Boss FATE", Position=new(400,0,0) };
    var o=Options(); var none=new Dictionary<FateKey,double>();
    Assert(Selection.Choose(world with { Fates=[near,far] }, o, none, 0)?.Id==1);
    o.PriorityFates=[2];
    Assert(Selection.Choose(world with { Fates=[near,far] }, o, none, 0)?.Id==2);
    o.ExcludedFates=[2];
    Assert(Selection.Choose(world with { Fates=[near,far] }, o, none, 0)?.Id==1, "Ignore list still wins");
});
Test("Zone history counts time, completed FATEs, gemstones and spawn spots per zone", () =>
{
    var history=new ZoneHistory(); var fake=new Fake(); var r=new Runner(fake){ History=history };
    r.Start(Options(), world, 0);
    var inside=Engage(r);
    r.Tick(inside with { Fates=[fate with { Progress=100 }], Gemstones=16 },7);
    var z=history.For(1000);
    Assert(z.Fates==1 && z.Gems==16 && z.Seconds>0 && z.Spots.Values.Sum(s=>s.Count)==1, $"fates={z.Fates} gems={z.Gems} s={z.Seconds}");
    for(var i=0;i<3;i++) history.RecordSpot(1000,new(105,0,5));
    Assert(history.Hotspot(1000) is { } spot && Vector3.Distance(spot,new(103.75f,0,3.75f))<1);
});
Test("Preferring rewards lets a bonus FATE win over a nearer one, within the chosen distance", () =>
{
    var near=fate with { Id=1, Position=new(50,0,0) }; var bonus=fate with { Id=2, Bonus=true, Position=new(200,0,0) };
    var o=Options(); var none=new Dictionary<FateKey,double>();
    Assert(Selection.Choose(world with { Fates=[near,bonus] }, o, none, 0)?.Id==1, "Off by default: nearest first");
    o.PreferRewards=true; o.BonusWorthYalms=200;
    Assert(Selection.Choose(world with { Fates=[near,bonus] }, o, none, 0)?.Id==2);
    o.BonusWorthYalms=100;
    Assert(Selection.Choose(world with { Fates=[near,bonus] }, o, none, 0)?.Id==1, "Too far for what the bonus is worth");
});
Test("Zone time budget moves to the next route zone between FATEs", () =>
{
    var o=Options(); o.ReturnAfterFates=0; o.MinutesPerZone=10; o.Route=[zone1,zone2];
    var (r,f)=Started(world,o); r.Tick(world,1); Assert(r.Selected!=null && f.Teleports.Count==0);
    var idle=world with { Fates=[] };
    r.Tick(idle,700); r.Tick(idle,701);
    Assert(f.Teleports.Single()==zone2);
});
Test("With nothing eligible, the runner waits at the zone's usual FATE spot", () =>
{
    var history=new ZoneHistory(); for(var i=0;i<3;i++) history.RecordSpot(1000,new(500,10,500));
    var fake=new Fake(); var r=new Runner(fake){ History=history }; var o=Options(); o.ReturnAfterFates=0;
    var idle=world with { Fates=[] };
    r.Start(o, idle, 0); r.Tick(idle,1); r.Tick(idle,2);
    Assert(fake.Moves==1 && Vector3.Distance(fake.LastMove,new(500,10,500))<1);
    r.Tick(idle,3); Assert(fake.Moves==1, "Only once per wait");
});
Test("A recoverable interruption releases control, waits and resumes, within the retry limit", () =>
{
    var o=Options(); o.AutoRetry=true; o.MaxRetries=1; o.ReturnAfterFates=0;
    var (r,f)=Started(world,o); r.Tick(world,1);
    r.Tick(world with { Territory=1001 },2);
    Assert(r.State==RunState.Retrying && f.Suspends==1 && f.Notifications.Any(n=>n.Kind==NotifyKind.Problem));
    r.Tick(world with { Territory=1001, DialogueOpen=true },15); Assert(r.State==RunState.Retrying, "Waits for the game to settle");
    r.Tick(world with { Territory=1001 },16);
    Assert(r.State==RunState.Selecting && f.Resumes==1 && r.IsRunning);
    r.Tick(world with { Territory=1001 },17); Assert(r.IsRunning, "The new zone is adopted");
    r.Tick(world with { Territory=1002 },30);
    Assert(!r.IsRunning, "Second interruption beyond the limit stops");
});
Test("Death and duty entry never retry", () =>
{
    var o=Options(); o.AutoRetry=true;
    var (r,_)=Started(world,o); r.Tick(world with { Dead=true },1); Assert(!r.IsRunning);
    (r,_)=Started(world,o); r.Tick(world with { Restricted=true },1); Assert(!r.IsRunning);
});
Test("Notifications: stop, goal and a prioritized FATE appearing; none for a user stop", () =>
{
    var o=Options(); o.PriorityFates=[1];
    var (r,f)=Started(world with { Fates=[] },o);
    r.Tick(world,1);
    Assert(f.Notifications.Any(n=>n.Kind==NotifyKind.PriorityFate));
    var inside=Engage(r);
    r.Tick(inside with { Fates=[fate with { Progress=100 }] },7); r.Tick(inside with { InCombat=false, Fates=[] },8);
    Assert(f.Notifications.Any(n=>n.Kind==NotifyKind.Goal));
    r.Stop(); Assert(!f.Notifications.Any(n=>n.Kind==NotifyKind.Stopped), "User stop is not announced");
    (r,f)=Started(world,o); r.Tick(world with { Dead=true },1);
    Assert(f.Notifications.Any(n=>n.Kind==NotifyKind.Stopped));
    o.NotifyOnStop=false; (r,f)=Started(world,o); r.Tick(world with { Dead=true },1);
    Assert(!f.Notifications.Any(n=>n.Kind==NotifyKind.Stopped), "Respects the setting");
});
Test("Session summary is kept after stopping", () =>
{
    var (r,_)=Started(); var inside=Engage(r);
    r.Tick(inside with { Fates=[fate with { Progress=100 }] },7);
    r.Stop();
    Assert(r.LastSummary is { Completed: 1 } s && s.Reason.Length > 0);
});
Test("Skip ignores the current FATE and selects again", () =>
{
    var (r,f)=Started(); r.Tick(world,1); Assert(r.Selected?.Id==1);
    r.Skip(2);
    Assert(r.Selected==null && r.State==RunState.Selecting && r.IsRunning);
    r.Tick(world,3); Assert(r.Selected==null, "Skipped FATE is excluded for a while");
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
Test("Crafter or gatherer jobs are refused before any module is touched", () =>
{
    foreach(var job in new uint[]{8,15,16,18})
    {
        var r=new Runner(new Fake()); r.Start(Options(), world with { Job=job }, 0);
        Assert(!r.IsRunning, r.Status);
    }
});
Test("Job change stops and releases ownership", () => { var (r,f)=Started(); r.Tick(world with { Job=23 },1); Assert(r.State==RunState.Faulted && f.Ends==1); });
Test("Unexpected loading stops before movement", () => { var (r,f)=Started(); r.Tick(world with { Loading=true },1); Assert(r.State==RunState.Faulted && f.Moves==0); });
Test("An unexpected dialog stops the session", () => { var (r,f)=Started(); r.Tick(world with { Occupied=true },1); Assert(!r.IsRunning && f.Ends==1); });
Test("Other travel automation interrupts the session", () => { var (r,f)=Started(); r.Tick(world with { TravelBusy=true },1); Assert(r.State==RunState.Faulted && f.Ends==1); });

Test("Earned gemstone goal excludes initial balance and returns without starting a FATE", () =>
{
    var o=Options(); o.ReturnAfterFates=0; o.ReturnAfterGemstones=20;
    var initial=world with{Gemstones=1000}; var (r,rt)=Started(initial,o);
    r.Tick(initial with{Gemstones=1020},1);
    Assert(r.GemstonesEarned==20 && r.PendingReturnGoal==ReturnGoal.Gemstones && rt.Teleports.SequenceEqual(new[]{home}));
});
Test("Time goal waits for the current combat FATE and confirms return", () =>
{
    var o=Options(); o.ReturnAfterFates=0; o.ReturnAfterMinutes=1;
    var (r,rt)=Started(o:o); var inside=Engage(r);
    r.Tick(inside,60); Assert(r.PendingReturnGoal==ReturnGoal.Time && r.IsRunning && rt.Teleports.Count==0);
    r.Tick(inside with{InCombat=false,Fates=[fate with{State=EventState.Ended,Progress=100}]},61);
    r.Tick(world,62); Assert(rt.Teleports.Count==1);
    r.Tick(world with{Loading=true},63);
    r.Tick(world with{Territory=129},64); Assert(r.State==RunState.Finished && r.ElapsedSeconds==64);
    r.Tick(world,100); Assert(r.ElapsedSeconds==64);
});
Test("A reached goal flushes a partial collection batch before returning", () =>
{
    var o=Options(); o.ReturnAfterFates=0; o.ReturnAfterMinutes=1;
    var collect=fate with{Kind=FateKind.Collection,HeldItems=2,Position=Vector3.Zero};
    var npc=new ObjectiveActor(502,1,ObjectiveRole.Recipient,Vector3.Zero);
    var w=world with{Fates=[collect],CurrentFate=1,Synced=true,Objectives=[npc]};
    var (r,rt)=Started(w,o);
    for(var i=1;i<=6;i++) r.Tick(w,i);
    Assert(rt.Interactions.Count==0);
    r.Tick(w,60); Assert(rt.Interactions.Count==1 && rt.Teleports.Count==0 && r.PendingReturnGoal==ReturnGoal.Time);
    r.Tick(w with{Fates=[collect with{HeldItems=0,HandedIn=2}]},61);
    Assert(rt.Teleports.Count==0 && r.IsRunning);
    r.Tick(w with{Fates=[collect with{HeldItems=0,HandedIn=2,State=EventState.Ended,Progress=100}]},62);
    r.Tick(w,63); Assert(rt.Teleports.Count==1 && r.Completed==1);
});
Test("First reached goal remains latched when another becomes true", () =>
{
    var o=Options(); o.ReturnAfterFates=0; o.ReturnAfterMinutes=1; o.ReturnAfterGemstones=10;
    var (r,rt)=Started(o:o); var w=Engage(r);
    r.Tick(w,60); r.Tick(w with{Gemstones=20},61);
    Assert(r.PendingReturnGoal==ReturnGoal.Time && rt.Teleports.Count==0);
    r.Stop(); r.Start(o,world,100); Assert(r.PendingReturnGoal==ReturnGoal.None && r.ElapsedSeconds==0 && r.GemstonesEarned==0);
});
Test("Currency reserve uses the live cap and deterministic simultaneous-goal priority", () =>
{
    var o=Options(); o.AvoidGemstoneCap=true; o.GemstoneCapReserve=200; o.ReturnAfterGemstones=10; o.ReturnAfterMinutes=1;
    Assert(SessionGoals.Evaluate(o,1,10,60,1299,1500)==ReturnGoal.Fates);
    Assert(SessionGoals.Evaluate(o,1,10,60,1300,1500)==ReturnGoal.CurrencyCap);
    Assert(SessionGoals.Evaluate(o,0,0,0,1300,2000)==ReturnGoal.None);
    var (r,rt)=Started(world with{Gemstones=1300,GemstoneCap=1500},o);
    r.Tick(world with{Gemstones=1300,GemstoneCap=1500},1);
    Assert(rt.Teleports.Count==1 && r.PendingReturnGoal==ReturnGoal.CurrencyCap);
});
Test("Unknown currency cap and oversized reserves refuse cap protection", () =>
{
    var o=Options(); o.AvoidGemstoneCap=true;
    var rt=new Fake(); var r=new Runner(rt); r.Start(o,world,0); Assert(r.State==RunState.Faulted);
    o.GemstoneCapReserve=1500; r.Start(o,world with{GemstoneCap=1500},0); Assert(r.State==RunState.Faulted);
    o.GemstoneCapReserve=200; r.Start(o,world with{GemstoneCap=1500},0); Assert(r.IsRunning);
    r.Tick(world,1); Assert(r.State==RunState.Faulted && rt.Teleports.Count==0);
});
Test("All goal types require a destination and reject invalid numeric configuration", () =>
{
    Throws(()=>new RunOptions{ReturnAfterFates=0,ReturnAfterGemstones=1}.Validate());
    Throws(()=>new RunOptions{ReturnAfterFates=0,ReturnAfterMinutes=1}.Validate());
    Throws(()=>new RunOptions{ReturnAfterFates=0,AvoidGemstoneCap=true}.Validate());
    Throws(()=>new RunOptions{ReturnDestination=home,ReturnAfterMinutes=-1}.Validate());
    Throws(()=>new RunOptions{ReturnDestination=home,ReturnAfterGemstones=-1}.Validate());
    Throws(()=>new RunOptions{ReturnDestination=home,GemstoneCapReserve=-1}.Validate());
});
Test("Reaching time goal during zone travel finishes that transition then returns", () =>
{
    var o=Options(); o.ReturnAfterFates=0; o.ReturnAfterMinutes=1; o.Route=[zone2];
    var (r,rt)=Started(o:o); r.Tick(world,1); r.Tick(world with{Loading=true},60);
    r.Tick(world with{Territory=1001},61); r.Tick(world with{Territory=1001},62);
    Assert(rt.Teleports.SequenceEqual(new[]{zone2,home}) && r.PendingReturnGoal==ReturnGoal.Time && rt.Instances.Count==0);
});
Test("Tracker world changes with visits and clears during transitions", () =>
{
    var first=TrackerWorld.FromCurrent(78,"Behemoth","Primal",true,false);
    var visited=TrackerWorld.FromCurrent(93,"Excalibur","Primal",true,false);
    Assert(first?.Name=="Behemoth" && visited?.Name=="Excalibur" && visited.Id!=first.Id);
    Assert(TrackerWorld.FromCurrent(93,"Excalibur","Primal",true,true)==null);
    Assert(TrackerWorld.FromCurrent(0,"","",true,false)==null);
    Assert(TrackerWorld.FromCurrent(93,"Excalibur","Primal",false,false)==null);
});

Test("Nearby NPC-started FATE is not ranked below an already running event", () =>
{
    var pending=fate with{Id=2,State=EventState.Preparing,CanStart=true,Position=new(10,0,0),SecondsLeft=0};
    Assert(Selection.Choose(world with{Fates=[fate with{Bonus=true},pending]},Options(),new Dictionary<FateKey,double>(),0)?.Id==2);
});
Test("FATE NPC binding resolves both server entity IDs and local event-object handles", () =>
{
    var npc=new FateNpcCandidate(0x100001234,0xE0000000,0x1234,20,false,true,true,false,0,Vector3.Zero,1);
    Assert(FateNpcBinding.Resolve(0x1234,0,Vector3.Zero,50,[npc])?.Id==npc.Id);
    Assert(FateNpcBinding.Resolve(55,0,Vector3.Zero,50,[npc with{EntityId=55}])?.Id==npc.Id);
});
Test("FATE NPC fallback requires a unique native event association", () =>
{
    var npc=new FateNpcCandidate(42,42,5,20,true,true,true,false,7,Vector3.Zero,1);
    Assert(FateNpcBinding.Resolve(0,7,Vector3.Zero,50,[npc])?.Id==42);
    Assert(FateNpcBinding.Resolve(0,7,Vector3.Zero,50,[npc with{Linked=false}])==null);
    Assert(FateNpcBinding.Resolve(0,7,Vector3.Zero,50,[npc,npc with{Id=43}])==null);
    Assert(FateNpcBinding.Resolve(0,7,Vector3.Zero,50,[npc,npc with{Id=43,Marker=8}])?.Id==42);
});
Test("FATE NPC binding rejects unavailable, distant, hostile or ambiguous fallback actors", () =>
{
    var npc=new FateNpcCandidate(42,42,5,20,true,true,true,false,7,Vector3.Zero,1);
    foreach(var candidate in new[]{npc with{Targetable=false},npc with{Alive=false},npc with{Hostile=true},npc with{Position=new(1000,0,0)}})
        Assert(FateNpcBinding.Resolve(0,7,Vector3.Zero,50,[candidate])==null);
    Assert(FateNpcBinding.Resolve(42,7,Vector3.Zero,50,[npc,npc with{Id=43}])==null);
});
Test("Faloop link follows current world and has a generic fallback", () =>
{
    Assert(FaloopLink.ForWorld(new(78,"Behemoth","Primal"))=="https://faloop.app/behemoth/fate");
    Assert(FaloopLink.ForWorld(new(93,"Excalibur","Primal"))=="https://faloop.app/excalibur/fate");
    Assert(FaloopLink.ForWorld(null)=="https://faloop.app/fate");
});
Test("Faloop world names cannot inject a host, path, query or fragment", () =>
{
    foreach(var name in new[]{"","a",new string('a',33),"//evil.example","x@evil.example","Behemoth/fate","Behemoth?name=x","Behemoth#x","%2f","Behemoth\\x","Behemoth\n","Ｂehemoth","../"})
        Assert(FaloopLink.ForWorld(new(1,name,"DC"))=="https://faloop.app/fate",name);
    Assert(FaloopLink.ForWorld(new(0,"Behemoth","Primal"))=="https://faloop.app/fate");
});
Test("Live route edit preserves the current FATE and session counters", () =>
{
    var o=Options(); o.ReturnAfterFates=0; o.Route=[zone1];
    var (r,rt)=Started(o:o); var inside=Engage(r);
    var changed=Options(); changed.ReturnAfterFates=0; changed.Route=[zone2]; changed.UseTickets=true;
    r.QueueOptions(changed); changed.Route.Clear();
    r.Tick(inside with{Gemstones=12},7);
    Assert(r.State==RunState.Fighting && r.HasPendingOptions && rt.Settings.Count==0 && rt.Teleports.Count==0);
    var done=inside with{InCombat=false,Gemstones=12,Fates=[fate with{State=EventState.Ended,Progress=100}]};
    r.Tick(done,8); r.Tick(done,9);
    Assert(r.Completed==1 && r.GemstonesEarned==12 && !r.HasPendingOptions && rt.Settings.Single().UseTickets);
    Assert(rt.Teleports.SequenceEqual(new[]{zone2}) && rt.Ends==0);
});
Test("Latest live edit wins and malformed edits leave the session running", () =>
{
    var o=Options(); o.ReturnAfterFates=0; var (r,rt)=Started(o:o);
    var changed=Options(); changed.ReturnAfterFates=0; changed.Route=[zone2];
    r.QueueOptions(changed); changed.Route=[zone1,zone2]; r.QueueOptions(changed);
    r.Tick(world,1); Assert(rt.Settings.Single().Route.SequenceEqual(new[]{zone1,zone2}) && rt.Teleports.Count==0);
    changed.Route=[zone1,zone1]; r.QueueOptions(changed);
    Assert(!r.HasPendingOptions && r.OptionsError!=null && r.IsRunning);
});
Test("Runtime refusal of live settings preserves previous options and does not stop combat", () =>
{
    var o=Options(); o.ReturnAfterFates=0; var (r,rt)=Started(o:o); rt.FailSettings=true;
    var changed=Options(); changed.ReturnAfterFates=0; changed.Route=[zone2]; r.QueueOptions(changed);
    r.Tick(world,1);
    Assert(r.IsRunning && r.OptionsError!=null && rt.Teleports.Count==0 && r.Selected?.Id==fate.Id);
});
Test("Live settings wait for teleport arrival and do not change the accepted destination", () =>
{
    var o=Options(); o.ReturnAfterFates=0; o.Route=[zone2]; var (r,rt)=Started(o:o); r.Tick(world,1);
    var changed=Options(); changed.ReturnAfterFates=0; changed.Route=[zone1]; r.QueueOptions(changed);
    r.Tick(world with{Loading=true},2); Assert(rt.Settings.Count==0);
    r.Tick(world with{Territory=1001},3); Assert(rt.Settings.Count==0 && rt.Teleports.SequenceEqual(new[]{zone2}));
    r.Tick(world with{Territory=1001},4); Assert(rt.Settings.Count==1 && rt.Teleports.SequenceEqual(new[]{zone2,zone1}));
});
Test("Live filters and goals apply before the next FATE selection", () =>
{
    var o=Options(); o.ReturnAfterFates=0; var (r,rt)=Started(o:o);
    var changed=Options(); changed.ReturnAfterFates=0; changed.ExcludedFates=[fate.Id]; r.QueueOptions(changed);
    r.Tick(world,1); Assert(r.Selected==null && r.State==RunState.Waiting);
    changed.ReturnAfterMinutes=1; r.QueueOptions(changed); r.Tick(world,60);
    Assert(r.PendingReturnGoal==ReturnGoal.Time && rt.Teleports.Single()==home);
});
Test("Live instance preference updates without rebuilding the session", () =>
{
    var o=Options(); o.ReturnAfterFates=0; o.Route=[zone1,zone2]; var empty=world with{Fates=[]};
    var (r,rt)=Started(empty,o); r.Tick(empty,1);
    o.RotateInstances=false; r.QueueOptions(o); r.Tick(empty,12);
    Assert(rt.Teleports.Single()==zone2 && rt.Instances.Count==0 && rt.Ends==0);
});
Test("Combat on the way to a FATE targets only attackers then resumes", () =>
{
    var (r,rt)=Started(); r.Tick(world,1); r.Tick(world,2);
    var attacked=world with{InCombat=true,Enemies=[new(77,0,Vector3.Zero,1,true),new(88,0,Vector3.One,1,false)]};
    r.Tick(attacked,3);
    Assert(r.State==RunState.Defending && rt.LastTarget==77 && rt.CombatEnabled && rt.Ends==0 && r.Completed==0);
    r.Tick(world,4); Assert(r.State==RunState.Mounting && !rt.CombatEnabled);
    r.Tick(world,5); Assert(r.State==RunState.Moving && r.Selected?.Id==fate.Id);
});
Test("Outside-FATE combat at sync recovers instead of terminating", () =>
{
    var (r,rt)=Started(); r.Tick(world,1); r.Tick(world,2);
    var close=world with{Position=fate.Position}; r.Tick(close,3); r.Tick(close,4);
    Assert(r.State==RunState.Syncing);
    r.Tick(close with{InCombat=true,Enemies=[new(77,0,fate.Position,1,true)]},5);
    Assert(r.State==RunState.Defending && rt.LastTarget==77 && r.IsRunning);
});
Test("Defense waits for targetable attackers and retains a finite deadline", () =>
{
    var (r,rt)=Started(); r.Tick(world,1);
    var attacked=world with{InCombat=true,Enemies=[new(88,0,Vector3.One,1,false)]};
    r.Tick(attacked,2); Assert(r.IsRunning && rt.LastTarget==0 && !rt.CombatEnabled);
    r.Tick(attacked,123); Assert(r.State==RunState.Faulted);
});
Test("Defense keeps known group-boss exclusions", () =>
{
    var (r,rt)=Started(); r.Tick(world,1);
    r.Tick(world with{InCombat=true,Enemies=[new(88,1855,Vector3.Zero,1,true)]},2);
    Assert(!r.IsRunning && rt.LastTarget==0);
});
Test("Forlorn priority preempts a closer attacker and returns to normal targeting", () =>
{
    foreach(var id in new uint[]{6737,6738})
    {
        var (r,rt)=Started(); var inside=Engage(r);
        var maiden=new EnemyInfo(500,1,fate.Position+new Vector3(15,0,0),1,false){NameId=id};
        r.Tick(inside with{Enemies=[new(99,1,fate.Position,1,true),maiden]},7);
        Assert(rt.LastTarget==500 && rt.CombatEnabled && r.State==RunState.Fighting);
        r.Tick(inside,8); Assert(rt.LastTarget==99 && r.IsRunning);
    }
});
Test("Forlorn takes priority over collection hand-ins and escort movement", () =>
{
    foreach(var kind in new[]{FateKind.Collection,FateKind.Escort})
    {
        var f=fate with{Kind=kind,HeldItems=15};
        var w=world with{Fates=[f],Position=fate.Position,CurrentFate=1,Synced=true,Objectives=[new(501,1,ObjectiveRole.Recipient,fate.Position)]};
        var (r,rt)=Started(w); for(var i=1;i<=4;i++)r.Tick(w,i);
        r.Tick(w with{Enemies=[new EnemyInfo(500,0,fate.Position,1,false){NameId=6737}]},5);
        r.Tick(w with{Enemies=[new EnemyInfo(500,0,fate.Position,1,false){NameId=6737}]},6);
        Assert(rt.LastTarget==500 && rt.CombatEnabled && rt.Interactions.Count==0);
    }
});
Test("Forlorn matching rejects other FATEs, unrelated names and distant unassigned enemies", () =>
{
    foreach(var maiden in new[]{new EnemyInfo(500,2,fate.Position,1,false){NameId=6737},new(500,0,new(999,0,0),1,false){NameId=6737},new(500,0,fate.Position,1,false){NameId=12349}})
    {
        var (r,rt)=Started(); var inside=Engage(r);
        r.Tick(inside with{Enemies=[new(99,1,fate.Position,1,false),maiden]},7);
        Assert(rt.LastTarget==99);
    }
});

RunOptions GreensOptions(int threshold=10,int target=99) => new()
{
    Route=[zone1], ReturnDestination=home, ReturnAfterFates=0, UseMount=false,
    RestockGreens=true, GreensThreshold=threshold, GreensTarget=target, GreensGilLimit=100000,
};
(Runner Runner,Fake Runtime,World Shop) AtGreensShop(RunOptions? options=null,int count=10)
{
    var w=world with{GreensInventory=new(true,count,999,100000)};
    var (r,rt)=Started(w,options??GreensOptions());
    r.Tick(w,1); Assert(rt.Teleports.SequenceEqual(new[]{GreensRestock.VendorCity}));
    r.Tick(w with{Loading=true},2);
    var shop=w with{Territory=129,Position=GreensRestock.VendorPosition,GreensVendor=new(700,GreensRestock.VendorPosition)};
    r.Tick(shop,3); r.Tick(shop,4); Assert(rt.ShopOpens==1);
    r.Tick(shop with{GreensShop=GreensShopState.Opening,Occupied=true,DialogueOpen=true},5); Assert(rt.ShopAdvances==1);
    shop=shop with{GreensShop=GreensShopState.Ready,Occupied=true,DialogueOpen=true};
    r.Tick(shop,6); return (r,rt,shop);
}
Test("Companion summoning defaults on while preserving an explicit saved off choice", () =>
{
    Assert(new RunOptions().SummonChocobo);
    Assert(JsonSerializer.Deserialize<RunOptions>("{}")!.SummonChocobo);
    Assert(!JsonSerializer.Deserialize<RunOptions>("{\"SummonChocobo\":false}")!.SummonChocobo);
});
Test("Companion summoning requires enablement, known inactive state, stock and game permission", () =>
{
    var w=world with{Fates=[],GreensInventory=new(true,10,1000,100),CompanionSecondsLeft=0,CanSummonChocobo=true};
    var o=Options();o.ReturnAfterFates=0;
    var (r,rt)=Started(w,o);r.Tick(w,1);Assert(rt.GreensUses==0);
    o.SummonChocobo=true;
    foreach(var blocked in new[]{w with{CompanionSecondsLeft=null},w with{CompanionSecondsLeft=1200},
        w with{GreensInventory=new(true,0,1000,100)},w with{GreensInventory=new(false,10,1000,100)},
        w with{CanSummonChocobo=false},w with{Mounted=true},w with{Flying=true}})
    {
        (r,rt)=Started(blocked,o);r.Tick(blocked,1);Assert(rt.GreensUses==0);
    }
});
Test("One Greens use waits for confirmation then resumes and does not extend an active companion", () =>
{
    var w=world with{GreensInventory=new(true,10,1000,100),CompanionSecondsLeft=0,CanSummonChocobo=true};
    var o=Options();o.SummonChocobo=true;
    var (r,rt)=Started(w,o);r.Tick(w,1);r.Tick(w,2);
    r.Tick(w with{Casting=true},3);r.Tick(w with{Occupied=true},4);
    Assert(rt.GreensUses==1 && rt.Moves==0 && r.IsRunning);
    w=w with{CompanionSecondsLeft=1800,GreensInventory=new(true,9,1000,100)};
    r.Tick(w,5);Assert(r.State==RunState.Mounting);
    r.Tick(w,6);r.Tick(w with{Position=fate.Position,CurrentFate=1},7);
    r.Tick(w with{Position=fate.Position,CurrentFate=1},8);
    Assert(rt.GreensUses==1 && r.State==RunState.Syncing);
});
Test("Unconfirmed Greens use resumes the route and is never repeated in the session", () =>
{
    var w=world with{Fates=[],GreensInventory=new(true,10,1000,100),CompanionSecondsLeft=0,CanSummonChocobo=true};
    var o=Options();o.SummonChocobo=true;o.ReturnAfterFates=0;
    var (r,rt)=Started(w,o);r.Tick(w,1);
    w=w with{GreensInventory=new(true,9,1000,100)};
    r.Tick(w,5);Assert(rt.GreensUses==1);
    r.Tick(w,11);r.Tick(w,100);r.Tick(w,1000);
    Assert(r.IsRunning && r.State==RunState.Waiting && rt.GreensUses==1);
});
Test("A rejected item request does not block travel or retry every tick", () =>
{
    var w=world with{Fates=[],GreensInventory=new(true,10,1000,100),CompanionSecondsLeft=0,CanSummonChocobo=true};
    var o=Options();o.SummonChocobo=true;o.ReturnAfterFates=0;
    var (r,rt)=Started(w,o);rt.RejectGreens=true;
    r.Tick(w,1);r.Tick(w,2);r.Tick(w,60);
    Assert(r.State==RunState.Waiting && rt.GreensUses==1);
    r.Tick(w,61);Assert(rt.GreensUses==2);
});
Test("A mounted start summons only after landing and before the NPC interaction", () =>
{
    var pending=fate with{State=EventState.Preparing,CanStart=true,SecondsLeft=0,StartedAt=0,Position=Vector3.Zero};
    var w=world with{Fates=[pending],Objectives=[new(501,1,ObjectiveRole.Starter,Vector3.Zero)],CurrentFate=1,
        Mounted=true,GreensInventory=new(true,10,1000,100),CompanionSecondsLeft=0,CanSummonChocobo=true};
    var o=Options();o.SummonChocobo=true;
    var (r,rt)=Started(w,o);for(var i=1;i<=4;i++)r.Tick(w,i);
    Assert(r.State==RunState.Landing && rt.GreensUses==0);
    w=w with{Mounted=false};r.Tick(w,5);Assert(rt.GreensUses==1 && rt.Interactions.Count==0);
    w=w with{CompanionSecondsLeft=1800};r.Tick(w,6);r.Tick(w,7);
    Assert(r.State==RunState.StartingEvent && rt.Interactions.Count==1 && rt.GreensUses==1);
});
Test("Combat, Stop, death and return goals prevent additional companion actions", () =>
{
    var w=world with{GreensInventory=new(true,10,1000,100),CompanionSecondsLeft=0,CanSummonChocobo=true};
    var o=Options();o.SummonChocobo=true;
    foreach(var interruption in new[]{w with{InCombat=true},w with{Dead=true},w with{Occupied=true},w with{Casting=true}})
    {
        var (r,rt)=Started(w,o);r.Tick(interruption,1);Assert(rt.GreensUses==0);
    }
    var (stopped,srt)=Started(w,o);stopped.Tick(w,1);stopped.Stop();stopped.Tick(w,100);Assert(srt.GreensUses==1);
    o.ReturnAfterMinutes=1;
    var (returning,rrt)=Started(w,o);returning.Tick(w,60);Assert(rrt.GreensUses==0 && rrt.Teleports.Count==1);
});
Test("Companion settings apply live and wait until an accepted use finishes", () =>
{
    var w=world with{Fates=[],GreensInventory=new(true,10,1000,100),CompanionSecondsLeft=0,CanSummonChocobo=true};
    var o=Options();o.ReturnAfterFates=0;
    var (r,rt)=Started(w,o);r.Tick(w,1);
    o.SummonChocobo=true;r.QueueOptions(o);r.Tick(w,2);Assert(rt.GreensUses==1);
    o.SummonChocobo=false;r.QueueOptions(o);r.Tick(w,3);Assert(r.HasPendingOptions);
    r.Tick(w with{CompanionSecondsLeft=1800},4);Assert(!r.HasPendingOptions);
    r.Tick(w,100);Assert(rt.GreensUses==1);
});
Test("A confirmed companion can be summoned again after its timer expires", () =>
{
    var w=world with{Fates=[],GreensInventory=new(true,10,1000,100),CompanionSecondsLeft=0,CanSummonChocobo=true};
    var o=Options();o.SummonChocobo=true;o.ReturnAfterFates=0;
    var (r,rt)=Started(w,o);r.Tick(w,1);
    r.Tick(w with{CompanionSecondsLeft=1800},2);
    r.Tick(w with{CompanionSecondsLeft=1},1801);Assert(rt.GreensUses==1);
    r.Tick(w with{GreensInventory=new(true,9,1000,100)},1802);Assert(rt.GreensUses==2);
});
Test("An attacker interrupts companion waiting and does not trigger another item use", () =>
{
    var w=world with{Fates=[],GreensInventory=new(true,10,1000,100),CompanionSecondsLeft=0,CanSummonChocobo=true};
    var o=Options();o.SummonChocobo=true;o.ReturnAfterFates=0;
    var (r,rt)=Started(w,o);r.Tick(w,1);
    r.Tick(w with{InCombat=true,Enemies=[new(99,0,Vector3.Zero,1,true)]},2);
    Assert(r.State==RunState.Defending && rt.LastTarget==99 && rt.CombatEnabled && rt.GreensUses==1);
    r.Tick(w,12);r.Tick(w,100);Assert(r.IsRunning && rt.GreensUses==1);
});
Test("An existing Forlorn target takes priority over a companion summon", () =>
{
    var w=world with{Position=fate.Position,CurrentFate=1,Mounted=true,Synced=true,
        GreensInventory=new(true,10,1000,100),CompanionSecondsLeft=0,CanSummonChocobo=true};
    var o=Options();o.SummonChocobo=true;
    var (r,rt)=Started(w,o);for(var i=1;i<=4;i++)r.Tick(w,i);
    w=w with{Mounted=false,Enemies=[new(99,1,fate.Position,1,false){NameId=6737}]};
    r.Tick(w,5);r.Tick(w,6);r.Tick(w,7);
    Assert(rt.GreensUses==0 && rt.LastTarget==99 && rt.CombatEnabled);
});
Test("Greens trigger includes zero, one and user-selected stock thresholds", () =>
{
    foreach(var threshold in new[]{0,1,10,20,35,99})
    {
        var o=GreensOptions(threshold,Math.Max(100,threshold+1));
        var (r,rt)=Started(world with{GreensInventory=new(true,threshold,999,100000)},o);
        r.Tick(world with{GreensInventory=new(true,threshold,999,100000)},1);
        Assert(rt.Teleports.Count==1);
        (r,rt)=Started(world with{GreensInventory=new(true,threshold+1,999,100000)},o);
        r.Tick(world with{GreensInventory=new(true,threshold+1,999,100000)},1);
        Assert(rt.Teleports.Count==0 && r.Selected!=null);
    }
});
Test("Greens restock is opt-in and rejects invalid targets or missing return route", () =>
{
    var (r,rt)=Started(world with{GreensInventory=new(true,0,999,100000)});
    r.Tick(world,1); Assert(rt.Teleports.Count==0);
    var o=GreensOptions(); o.GreensTarget=o.GreensThreshold; Throws(o.Validate);
    o=GreensOptions(); o.GreensGilLimit=0; Throws(o.Validate);
    o=GreensOptions(); o.Route=[]; Throws(o.Validate);
    o=GreensOptions(999,1000); Throws(o.Validate);
});
Test("Greens budget, inventory space and loading are checked before departure", () =>
{
    foreach(var stock in new[]{new GreensInventory(true,10,2,100000),new(true,10,999,1),new(false,0,0,0)})
    {
        var (r,rt)=Started(world with{GreensInventory=stock},GreensOptions());
        r.Tick(world with{GreensInventory=stock},1); Assert(r.State==RunState.Faulted && rt.Teleports.Count==0);
    }
    var o=GreensOptions(); o.GreensGilLimit=100;
    var (runner,runtime)=Started(world with{GreensInventory=new(true,10,999,100000)},o);
    runner.Tick(world with{GreensInventory=new(true,10,999,100000)},1);
    Assert(runner.State==RunState.Faulted && runtime.Teleports.Count==0);
});
Test("Greens full trip confirms inventory and gil then resumes its original zone", () =>
{
    var (r,rt,s)=AtGreensShop(); r.Tick(s,7);
    Assert(rt.Purchases.Count==1 && rt.Purchases[0].Quantity==89 && r.GreensGilCommitted==3204);
    r.Tick(s with{GreensShop=GreensShopState.Busy},8); Assert(rt.Purchases.Count==1);
    s=s with{GreensInventory=new(true,99,910,96796)};
    r.Tick(s,9); r.Tick(s,10); Assert(rt.ShopCloses==1);
    r.Tick(s with{GreensShop=GreensShopState.Closed,Occupied=false,DialogueOpen=false},11);
    Assert(rt.Teleports.SequenceEqual(new[]{GreensRestock.VendorCity,zone1}));
    r.Tick(s with{Loading=true},12);
    var back=world with{GreensInventory=s.GreensInventory}; r.Tick(back,13); r.Tick(back,14);
    Assert(r.Selected?.Id==fate.Id && r.IsRunning && rt.Purchases.Count==1 && r.GreensGilCommitted==3204);
});
Test("Greens buys bounded batches and never repeats an unconfirmed purchase", () =>
{
    var (r,rt,s)=AtGreensShop(GreensOptions(10,200)); r.Tick(s,7);
    Assert(rt.Purchases.Single().Quantity==99);
    for(var i=8;i<=12;i++) r.Tick(s,i);
    Assert(rt.Purchases.Count==1);
    r.Tick(s with{GreensInventory=new(true,109,900,96436)},13);
    Assert(rt.Purchases.Count==2 && rt.Purchases[1].Quantity==91 && r.GreensGilCommitted==6840);
});
Test("Greens receipt tolerates separate gil and item updates but not a mismatch", () =>
{
    var p=new GreensPurchase(10,5,1000,0);
    Assert(p.Observe(new(true,5,999,640),1)==PurchaseResult.Pending);
    Assert(p.Observe(new(true,15,999,1000),1)==PurchaseResult.Pending);
    Assert(p.Observe(new(true,15,999,640),2)==PurchaseResult.Confirmed);
    Assert(p.Observe(new(true,16,999,640),2)==PurchaseResult.Mismatch);
    Assert(p.Observe(new(true,15,999,639),2)==PurchaseResult.Mismatch);
    Assert(p.Observe(new(true,5,999,1000),15)==PurchaseResult.TimedOut);
});
Test("Greens transaction timeout stops without buying again", () =>
{
    var (r,rt,s)=AtGreensShop(); r.Tick(s,7); r.Tick(s,22); r.Tick(s,23);
    Assert(r.State==RunState.Faulted && rt.Purchases.Count==1 && rt.Teleports.Count==1);
});
Test("Foreign shop context and early shop closure stop restocking", () =>
{
    foreach(var state in new[]{GreensShopState.Foreign,GreensShopState.Closed,GreensShopState.Failed})
    {
        var (r,rt,s)=AtGreensShop(); r.Tick(s with{GreensShop=state},7);
        Assert(!r.IsRunning && rt.Purchases.Count==0);
    }
});
Test("A session goal cancels unsent restocking and returns home", () =>
{
    var o=GreensOptions(); o.ReturnAfterMinutes=1;
    var (r,rt,s)=AtGreensShop(o); r.Tick(s,60); Assert(rt.Purchases.Count==0 && rt.ShopCloses==1);
    r.Tick(s with{GreensShop=GreensShopState.Closed,Occupied=false,DialogueOpen=false},61);
    Assert(rt.Teleports.SequenceEqual(new[]{GreensRestock.VendorCity,home}));
});
Test("A goal during a purchase waits for its receipt then stops buying", () =>
{
    var o=GreensOptions(10,200); o.ReturnAfterGemstones=10;
    var (r,rt,s)=AtGreensShop(o); r.Tick(s,7);
    r.Tick(s with{Gemstones=10},8); Assert(rt.ShopCloses==0 && rt.Purchases.Count==1);
    s=s with{Gemstones=10,GreensInventory=new(true,109,900,96436)};
    r.Tick(s,9); Assert(rt.ShopCloses==1 && rt.Purchases.Count==1);
});
Test("Low greens waits until the active FATE is over", () =>
{
    var stocked=world with{GreensInventory=new(true,100,999,100000)};
    var (r,rt)=Started(stocked,GreensOptions());
    r.Tick(stocked,1); r.Tick(stocked,2);
    var inside=stocked with{Position=fate.Position,CurrentFate=1,Synced=true};
    r.Tick(inside,3); r.Tick(inside,4); r.Tick(inside,5);
    inside=inside with{GreensInventory=new(true,0,999,100000),InCombat=true,Enemies=[new(99,1,fate.Position,1,false)]};
    r.Tick(inside,6); Assert(rt.Teleports.Count==0 && r.State==RunState.Fighting);
    inside=inside with{InCombat=false,Fates=[fate with{State=EventState.Ended,Progress=100}]};
    r.Tick(inside,7); r.Tick(inside,8); Assert(rt.Teleports.Count==1);
});
Test("Stop and death prevent further restock actions", () =>
{
    var (r,rt,s)=AtGreensShop(); r.Stop(); r.Tick(s,7); Assert(rt.Purchases.Count==0);
    (r,rt,s)=AtGreensShop(); r.Tick(s with{Dead=true},7); Assert(rt.Purchases.Count==0 && !r.IsRunning);
});
Test("Restock opening and navigation have finite deadlines", () =>
{
    var (r,rt,s)=AtGreensShop();
    // Opening an unrelated interaction during city travel is not adopted.
    var o=GreensOptions(); var w=world with{GreensInventory=new(true,0,999,100000)};
    (r,rt)=Started(w,o); r.Tick(w,1); r.Tick(w with{Loading=true},2);
    r.Tick(w with{Territory=129},3); r.Tick(w with{Territory=129,Occupied=true,DialogueOpen=true},4);
    Assert(!r.IsRunning && rt.Purchases.Count==0);
    (r,rt)=Started(w,o); r.Tick(w,1); r.Tick(w with{Loading=true},2);
    r.Tick(w with{Territory=129},3); r.Tick(w with{Territory=129},34); Assert(r.State==RunState.Faulted);
});

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
    Test("Source privacy guard and click-only fixed browser links", () => PrivacyAudit.Source(args[1]));
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
    public int Advances;
    public ulong LastTarget;
    public bool CombatEnabled;
    public Vector3 LastMove;
    public readonly List<ObjectiveActor> Interactions=[];
    public bool FailBegin, FailCombat;
    public readonly List<Destination> Teleports=[];
    public readonly List<int> Instances=[];
    public void Begin(RunOptions o) { if(FailBegin) throw new InvalidOperationException("partial setup failure"); }
    public readonly List<RunOptions> Settings = [];
    public bool FailSettings;
    public void UpdateOptions(RunOptions o) { if(FailSettings) throw new InvalidOperationException("invalid destination"); Settings.Add(o); }
    public void End()=>Ends++;
    public void Move(Vector3 p,bool fly) { Moves++; LastMove=p; }
    public void StopMovement() { }
    public void Mount() { }
    public void Dismount() { }
    public int Syncs;
    public void Sync() { Syncs++; }
    public void SetCombat(bool enabled) { if(enabled&&FailCombat) throw new InvalidOperationException("IPC gone"); CombatEnabled=enabled; }
    public void Target(ulong id) { LastTarget=id; }
    public void Teleport(Destination d)=>Teleports.Add(d);
    public void ConfirmTeleport()=>Confirmations++;
    public void ChangeInstance(int instance)=>Instances.Add(instance);
    public int Suspends, Resumes, FoodUses, RepairStarts;
    public readonly List<int> Gearsets=[];
    public bool RejectFood;
    public void Suspend()=>Suspends++;
    public void ResumeSession()=>Resumes++;
    public bool EquipGearset(int index){ Gearsets.Add(index); return true; }
    public bool UseFood(uint itemId,bool hq){ FoodUses++; return !RejectFood; }
    public bool StartRepair(){ RepairStarts++; return true; }
    public readonly List<(NotifyKind Kind, string Message)> Notifications=[];
    public void Notify(NotifyKind kind, string message)=>Notifications.Add((kind,message));
    public readonly List<uint> Shards=[];
    public void AethernetTeleport(uint shardId)=>Shards.Add(shardId);
    public void Log(string message) { }
    public void Interact(ObjectiveActor actor) => Interactions.Add(actor);
    public void AdvanceEventInteraction() => Advances++;
    public void CancelEventInteraction() { }
    public int ShopOpens, ShopAdvances, ShopCloses;
    public readonly List<GreensPurchase> Purchases=[];
    public void OpenGreensShop(VendorActor actor) => ShopOpens++;
    public void AdvanceGreensShop() => ShopAdvances++;
    public void BuyGreens(GreensPurchase purchase) => Purchases.Add(purchase);
    public void CloseGreensShop() => ShopCloses++;
    public int GreensUses;
    public bool RejectGreens;
    public bool UseGysahlGreens() { GreensUses++; return !RejectGreens; }
}
