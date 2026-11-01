using AgentRelay.Core;
using AgentRelay.Infrastructure;

internal static class ProjectLifecycleScenarios
{
    internal static async Task Run(string data)
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var owner = Guid.NewGuid(); var other = Guid.NewGuid(); var worker = Guid.NewGuid();
        var wa = Guid.NewGuid(); var wb = Guid.NewGuid(); var sessionId = Guid.NewGuid(); var token = CancellationToken.None;
        var rights = RelayCapabilities.All.Where(item => item != RelayCapabilities.ManageProject).ToArray();
        var configuration = new ProjectConfiguration(1, [new(a,"A"),new(b,"B")],
            [new(owner,"Main A","memory","main-a"),new(other,"Main B","memory","main-b"),new(worker,"Worker","memory","worker")],
            [new(wa,worker,"A","memory://a"),new(wb,worker,"B","memory://b")],
            [new(a,owner,"main",rights,[new(wa,true,true,true)],["list"]),new(a,worker,"secondary",[RelayCapabilities.ReceiveMessage],[]),
             new(b,other,"main",RelayCapabilities.All.ToArray(),[new(wb,true,true,true)],["list"]),new(b,worker,"secondary",[],[])]);
        var settings = new ProjectSettingsStore(data); settings.Save(configuration);
        var journal = new ProjectJournal(new RelayDatabase(data)); var store = new SqliteWorkStore(new RelayDatabase(data));
        var fake = new Adapter(new(sessionId,"Owned session",new(a,owner,worker,wa),"closed"));
        await using var work = new WorkService(store,fake,()=>[new(worker.ToString("D"),true,true,true)]);
        var app = new RelayApplication(new(settings),work,fake,journal,projectStates:settings);
        var caller = new CallerIdentity(owner,[a]); var foreign = new CallerIdentity(other,[b]);
        await Refuses("capability_denied",()=>app.SetProjectStateAsync(caller,a,false,true,token));
        configuration = configuration with { Memberships=configuration.Memberships.Select(item=>item.EndpointId==owner?item with {Capabilities=item.Capabilities.Append(RelayCapabilities.ManageProject).ToArray()}:item).ToArray() };
        settings.Save(configuration);
        await Refuses("capability_denied",()=>app.SetProjectStateAsync(foreign,a,false,true,token));
        var now = DateTimeOffset.UtcNow;
        var uncertain = new WorkRecord(new(Guid.NewGuid(),worker.ToString("D"),"transfer",Scope:new(a,owner,worker,wa)),"unknown",now,now);
        await store.SaveAsync(uncertain,token);
        for(var i=0;i<151;i++)await store.SaveAsync(uncertain with {Request=uncertain.Request with {Id=Guid.NewGuid(),Kind="command"},State="completed"},token);
        await Refuses("archive_blocked",()=>app.SetProjectStateAsync(caller,a,false,true,token));
        if(fake.Inspected!=0||!settings.Load().Projects.Single(p=>p.Id==a).Enabled)throw new Exception("Paged preflight missed old uncertain work or touched an adapter too early.");
        await store.SaveAsync(uncertain with {State="failed"},token);
        fake.State="ready";
        await Refuses("archive_blocked",()=>app.SetProjectStateAsync(caller,a,false,true,token));
        fake.State="closed";fake.Failure=true;
        await Refuses("adapter_unavailable",()=>app.SetProjectStateAsync(caller,a,false,true,token));
        fake.Failure=false;fake.Pause=true;
        var archive = app.SetProjectStateAsync(caller,a,false,true,token);
        await fake.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Refuses("busy",()=>app.MessageAsync(caller,sessionId,Guid.NewGuid(),"must not start",null,token));
        await Refuses("busy",()=>app.CommandAsync(caller,a,worker,wa,"list",Guid.NewGuid(),token));
        await Refuses("busy",()=>app.ControlSessionAsync(caller,sessionId,"reopen",token));
        try { using var lease=app.Admission.Configure(); throw new Exception("Full configuration raced archival."); }
        catch(RelayException error) when(error.Code=="busy") { }
        var otherWork=await app.CommandAsync(foreign,b,worker,wb,"list",Guid.NewGuid(),token);
        var deadline=DateTime.UtcNow.AddSeconds(3);
        while((await app.ResultAsync(foreign,otherWork.Request.Id,token)).State is "pending" or "running" && DateTime.UtcNow<deadline)await Task.Delay(10);
        fake.Release.TrySetResult();var archived=await archive;
        if(archived.Enabled||fake.Executed!=1)throw new Exception("Unrelated project was blocked or a forbidden project operation ran.");
        var discover=app.Discover(caller);
        if(discover.Projects.Single().State!="archived"||discover.Projects[0].Id!=a)throw new Exception("Archived discovery lost scope or status.");
        await Refuses("project_archived",()=>app.MessageAsync(caller,sessionId,Guid.NewGuid(),"blocked",null,token));
        await Refuses("project_archived",()=>app.CommandAsync(caller,a,worker,wa,"list",Guid.NewGuid(),token));
        await Refuses("project_archived",()=>app.ControlSessionAsync(caller,sessionId,"start",token));
        await Refuses("project_archived",()=>app.TransferAsync(caller,sessionId,new(new(worker,wa,"a"),new(worker,wa,"b")),Guid.NewGuid(),token));
        var inspections=fake.Inspected;
        await app.SessionAsync(caller,sessionId,token);await app.InboxAsync(caller,new(a),token);
        if(fake.Inspected!=inspections||fake.Refreshed!=0)throw new Exception("Archived history caused a native inspection/import.");
        if((await app.ResultsAsync(caller,new(a,Limit:100),token)).Items.Count!=100)throw new Exception("Archived history was hidden.");
        try{settings.Save(settings.Load() with {Projects=[new(a,"A"),new(b,"B")]});throw new Exception("Raw configuration bypassed lifecycle.");}
        catch(RelayException error) when(error.Code=="project_lifecycle_required") { }
        var afterRestart=new RelayApplication(new(new ProjectSettingsStore(data)),work,fake,journal,projectStates:new ProjectSettingsStore(data));
        if(afterRestart.Discover(caller).Projects.Single().Enabled)throw new Exception("Archived state did not persist.");
        var beforeCalls=fake.Executed;fake.Pause=false;
        await afterRestart.SetProjectStateAsync(caller,a,true,false,token);
        await Refuses("state_conflict",()=>afterRestart.SetProjectStateAsync(caller,a,false,false,token));
        if(fake.Executed!=beforeCalls||!settings.Load().Projects.Single(p=>p.Id==a).Enabled||!settings.Load().Memberships.SequenceEqual(configuration.Memberships,new MembershipComparer()))throw new Exception("Reactivation replayed work or altered permissions.");
        Console.WriteLine("PROJECT_LIFECYCLE=PASS (paged preflight, uncertain/ready/fault refusal, locked admission/configuration, unrelated-project concurrency, scoped archived history, restart and no replay)");
    }
    private static async Task Refuses(string code, Func<Task> action)
    {try{await action();throw new Exception("Expected lifecycle refusal: "+code);}catch(RelayException error) when(error.Code==code){} }
    private sealed class MembershipComparer : IEqualityComparer<ProjectMembership>
    {public bool Equals(ProjectMembership? x,ProjectMembership? y)=>RelayJson.Serialize(x)==RelayJson.Serialize(y);public int GetHashCode(ProjectMembership x)=>x.ProjectId.GetHashCode();}
    private sealed class Adapter(SessionSnapshot retained) : IRelaySessions,IWorkExecutor
    {
        public string State="closed";public bool Pause,Failure;public int Inspected,Executed,Refreshed;
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SessionSnapshot> GetAsync(Guid id,CancellationToken token)=>Task.FromResult(retained with {State=State});
        public async Task<SessionSnapshot> InspectAsync(Guid id,CancellationToken token){Inspected++;if(Failure)throw new RelayException("adapter_unavailable","Fixture unavailable");if(Pause){Entered.TrySetResult();await Release.Task.WaitAsync(token);}return await GetAsync(id,token);}
        public Task<ReadPage<SessionSnapshot>> AllAsync(ReadQuery query,CancellationToken token)=>Task.FromResult(new ReadPage<SessionSnapshot>(query.ProjectId==retained.Scope.ProjectId?[retained with {State=State}]:[],null,false));
        public Task<ReadPage<SessionSnapshot>> ListAsync(ReadQuery query,Guid owner,CancellationToken token)=>AllAsync(query,token);
        public Task<SessionSnapshot> OpenAsync(SessionDefinition definition,CancellationToken token)=>throw new Exception("Unexpected session start");
        public Task<SessionSnapshot> ControlAsync(Guid id,string action,CancellationToken token)=>throw new Exception("Unexpected session mutation");
        public Task<IReadOnlyList<string>> RefreshMessagesAsync(Guid project,Guid recipient,Guid? session,CancellationToken token){Refreshed++;return Task.FromResult<IReadOnlyList<string>>([]);}
        public Task<WorkResult> ExecuteAsync(WorkRequest request,CancellationToken token){Executed++;return Task.FromResult(new WorkResult("fixture","done"));}
    }
}
