using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed class EndpointDiagnostics(SettingsStore settings) : IHostInspector
{
    private const string Probe = """
        import json,os,pathlib,pwd,shutil,subprocess
        facts={'effectiveUser':pwd.getpwuid(os.getuid()).pw_name,'identity':str(os.getuid()),'platform':os.uname().sysname,'kernel':os.uname().release}
        for name in ['php','git','docker','tmux','codex','claude']:
            path=shutil.which(name)
            if not path and name=='claude':
                candidate=pathlib.Path.home()/'.local/bin/claude';path=str(candidate) if candidate.is_file() else None
            facts['tool.'+name+'.path']=path or 'unavailable'
            if path:
                try:
                    result=subprocess.run([path,'-V' if name=='tmux' else '--version'],capture_output=True,text=True,timeout=3)
                    facts['tool.'+name+'.version']=(result.stdout or result.stderr).splitlines()[0][:200] if result.returncode==0 else 'not confirmed'
                except (subprocess.TimeoutExpired,OSError,IndexError):facts['tool.'+name+'.version']='not confirmed'
        facts['dockerSocketPresent']=str(pathlib.Path('/var/run/docker.sock').exists()).lower()
        print(json.dumps(facts))
        """;
    public async Task<HostReport> InspectAsync(RelayEndpoint endpoint, CancellationToken token)
    {
        if (!endpoint.AdapterId.StartsWith("wsl-", StringComparison.Ordinal)) throw new RelayException("adapter_unavailable", "This endpoint has no installed host-inspection adapter.");
        var binding = settings.Load().Profiles.SingleOrDefault(profile => profile.Id == endpoint.BindingId) ?? throw new RelayException("adapter_unavailable", "Endpoint binding is unavailable.");
        var result = await LocalProcess.RunAsync("wsl.exe", ["-d", binding.Distribution, "-u", binding.User, "--exec", "/usr/bin/python3", "-c", Probe], null, TimeSpan.FromSeconds(30), token);
        if (result.ExitCode != 0) throw new RelayException("unavailable", "Host inspection did not complete; no configuration was changed.", effectsPossible: false);
        var facts = RelayJson.Deserialize<Dictionary<string, string>>(result.Output);
        if (facts.GetValueOrDefault("effectiveUser") != binding.User) throw new InvalidDataException("Host diagnostic identity was not confirmed.");
        return new(endpoint.Id, facts);
    }
}
