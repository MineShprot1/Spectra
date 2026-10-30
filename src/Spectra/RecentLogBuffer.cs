namespace Spectra;
// Crash diagnostics need a recent tail, not an unbounded copy of game output.
public sealed class RecentLogBuffer
{
 readonly Queue<string> lines=new();readonly object gate=new();int characters;
 public void Add(string line)
 {
  if(line.Length>8192)line=line[..8192];
  lock(gate){lines.Enqueue(line);characters+=line.Length+1;while(lines.Count>500||characters>262144)characters-=lines.Dequeue().Length+1;}
 }
 public override string ToString(){lock(gate)return string.Join("\n",lines);}
}
