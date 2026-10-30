using Spectra;
static class NotificationTests
{
 public static void Run()
 {
  var stack=new NotificationStack<object>();var items=Enumerable.Range(0,8).Select(_=>new object()).ToArray();
  for(var i=0;i<items.Length;i++){var audible=stack.Push(items[i]);if(audible!=(i<3)||stack.Visible.Count>3)throw new Exception("Notification admission limit/sound policy");}
  if(stack.PendingCount!=5)throw new Exception("Waiting notifications lost");
  var next=stack.Dismiss(items[1]);if(!ReferenceEquals(next,items[3])||stack.Visible.Count!=3||!ReferenceEquals(stack.Visible[^1],items[3]))throw new Exception("FIFO promotion must append at the bottom");
  stack.Clear();if(stack.Visible.Count!=0||stack.PendingCount!=0)throw new Exception("Hide all did not discard pending notifications");
  var fresh=new object();if(!stack.Push(fresh))throw new Exception("Fresh notification blocked after clear");
  if(stack.Dismiss(items[0])!=null||stack.Visible.Count!=1||!ReferenceEquals(stack.Visible[0],fresh))throw new Exception("Stale close callback modified the new batch");
  stack.Clear();for(var i=0;i<160;i++)stack.Push(new object());if(stack.Visible.Count!=3||stack.PendingCount!=100)throw new Exception("Queue is unbounded");
  Console.WriteLine("PASS notification stack: maximum three, FIFO bottom promotion, silent backlog, clear/open discards whole batch, fresh arrivals and stale callbacks");
 }
}
