using AgentRelay.Core;
using AgentRelay.Windows.Presentation;

internal static class AttentionReviewScenarios
{
    internal static void Run(string directory)
    {
        var project=Guid.NewGuid();var actor=Guid.NewGuid();var now=DateTimeOffset.UtcNow;
        var work=new WorkRecord(new(Guid.NewGuid(),"connector","transfer","",Scope:new(project,actor,Guid.NewGuid())),"failed",now,now,Error:"expected rejection",ErrorCode:"file_too_large");
        var store=new AttentionReviews(directory);
        store.Mark(work,AttentionReviews.Token(work));
        var reopened=new AttentionReviews(directory);
        if(!reopened.IsReviewed(work))throw new Exception("Review did not survive reopening");
        var evolved=work with { UpdatedAt=now.AddTicks(1) };
        var differentProject=work with { Request=work.Request with { Scope=work.Request.Scope! with { ProjectId=Guid.NewGuid() } } };
        if(reopened.IsReviewed(evolved)||reopened.IsReviewed(differentProject)||reopened.IsReviewed(work with { State="unknown" }))throw new Exception("Review hid another project or operation revision");
        try { reopened.Mark(evolved,AttentionReviews.Token(work));throw new Exception("Stale review accepted"); }
        catch(RelayException error) when(error.Code=="review_stale") { }
        if(work.State!="failed"||work.ErrorCode!="file_too_large")throw new Exception("Review changed historical outcome");
        var file=Path.Combine(directory,"attention-reviews.json");File.WriteAllText(file,"damaged review data");
        var corrupt=new AttentionReviews(directory);
        if(corrupt.Warning is null||corrupt.IsReviewed(work))throw new Exception("Damaged review file hid an alert");
        try { corrupt.Mark(work,AttentionReviews.Token(work));throw new Exception("Damaged review file overwritten"); }catch(IOException){}
        if(File.ReadAllText(file)!="damaged review data")throw new Exception("Damaged review data was discarded");
        Console.WriteLine("PASS: durable scoped review; changed/stale revisions visible; damaged review data preserved; history unchanged");
    }
}
