namespace Spectra;
// The model is independent of windows and timers: clearing a batch cannot promote
// one of its old queued items when a closing card's callback arrives later.
public sealed class NotificationStack<T> where T:class
{
 readonly List<T> visible=[];readonly Queue<T> pending=new();
 public IReadOnlyList<T> Visible=>visible;
 public int PendingCount=>pending.Count;
 public bool Push(T item){if(visible.Count<3){visible.Add(item);return true;}if(pending.Count>=100)pending.Dequeue();pending.Enqueue(item);return false;}
 public T? Dismiss(T item){if(!visible.Remove(item))return null;if(pending.Count==0)return null;var next=pending.Dequeue();visible.Add(next);return next;}
 public void Clear(){visible.Clear();pending.Clear();}
}
