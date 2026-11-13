using System.Reflection;
using System.Text.Json;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Length!=4)throw new ArgumentException("Provide prototype assets, fixture JSON, check script and output directory.");
        var assets=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[3]);Directory.CreateDirectory(output);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var window=new Form {Text="Agent Relay · Test de rendu · Maquette",ClientSize=new Size(1120,728),StartPosition=FormStartPosition.CenterScreen};
        var type=Assembly.Load("AgentRelay.Windows").GetType("AgentRelay.Windows.Presentation.RelayWebView",true)!;
        using var view=(Control)Activator.CreateInstance(type,true)!;view.Dock=DockStyle.Fill;window.Controls.Add(view);
        object? Invoke(string name,params object?[] values)=>type.GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!.Invoke(view,values);
        Task<string> Eval(string script)=>(Task<string>)Invoke("EvaluateForVerificationAsync",script)!;
        var code=0;
        window.Shown+=async(_,_)=>
        {
            try
            {
                await (Task)Invoke("InitializeAsync",Path.Combine(output,"browser-data"),assets)!;
                await Eval("window.__fixture="+File.ReadAllText(args[1])+";window.RelayUI.receive({type:'snapshot',data:structuredClone(__fixture)});"+File.ReadAllText(args[2]));
                foreach(var stage in new[]{"summary","preview","details","changed","partial","compact"})
                {
                    window.ClientSize=stage=="compact"?new Size(816,728):new Size(1120,728);
                    if(await Eval("window.CleanupChecks."+stage+"()")!="true")throw new Exception("Prototype scenario failed: "+stage);
                    await Eval("new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)))");
                    await (Task)Invoke("CaptureAsync",Path.Combine(output,stage+".png"))!;
                }
                await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new {passed=true,stages=6,ownWebViewCapture=true,realDeletions=0,applicationChanged=false}));
                Console.WriteLine("STORAGE_PROTOTYPE=PASS");
            }
            catch(Exception error){code=1;Console.Error.WriteLine(error);}
            finally{window.Close();}
        };
        Application.Run(window);return code;
    }
}
