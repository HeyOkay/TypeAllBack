using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace LayoutPreviewPrototype;

internal static class DebugHistoryBenchmark
{
    internal static void Run()
    {
        Exception? failure=null;
        var worker=new Thread(()=>
        {
            try
            {
                var results=new List<object>();
                foreach(bool incremental in new[]{false,true})
                {
                    GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                    var history=new RichTextBox {IsReadOnly=true,IsUndoEnabled=!incremental,Width=750,Height=820};
                    var renderer=new IncrementalHistory(history);
                    var queue=new Queue<SessionDiagnosticEvent>();
                    for(int i=1;i<=200;i++)queue.Enqueue(Event(i));
                    long allocated=GC.GetTotalAllocatedBytes(true);
                    var timer=Stopwatch.StartNew();
                    for(int tick=0;tick<300;tick++)
                    {
                        queue.Dequeue();queue.Enqueue(Event(201+tick));
                        var entries=queue.ToArray();
                        if(incremental) renderer.Update(entries,1);
                        else
                        {
                            history.BeginChange();
                            try
                            {
                                history.Document.Blocks.Clear();
                                foreach(var entry in entries) history.Document.Blocks.Add(new Paragraph(new Run(entry.ToString()){Foreground=Brushes.Gainsboro}) {Margin=new Thickness(0,0,0,3)});
                            }
                            finally{history.EndChange();}
                        }
                        history.Measure(new Size(750,820));history.Arrange(new Rect(0,0,750,820));history.UpdateLayout();
                    }
                    timer.Stop();
                    results.Add(new {mode=incremental?"incremental":"full-rebuild-with-undo",iterations=300,milliseconds=timer.Elapsed.TotalMilliseconds,allocatedMiB=(GC.GetTotalAllocatedBytes(true)-allocated)/1048576.0,paragraphs=history.Document.Blocks.Count});
                    GC.KeepAlive(history);
                }
                Console.WriteLine(JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
            }
            catch(Exception error){failure=error;}
        });
        worker.SetApartmentState(ApartmentState.STA);worker.Start();worker.Join();
        if(failure!=null)throw failure;
    }
    private static SessionDiagnosticEvent Event(int number)=>new(number,DateTime.UnixEpoch,1,"Ввод",new string('x',120));
}
