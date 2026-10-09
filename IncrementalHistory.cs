using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace LayoutPreviewPrototype;

internal sealed class IncrementalHistory(RichTextBox history)
{
    private readonly Dictionary<long, Paragraph> _paragraphs = [];
    private bool _initialized;
    private static readonly Brush BrightRed = ColorBrush(255,64,64);
    private static readonly Brush OldGreen = ColorBrush(70,150,70);
    private static readonly Brush OldRed = ColorBrush(184,92,92);
    private static Brush ColorBrush(byte r,byte g,byte b)
    { var brush=new SolidColorBrush(Color.FromRgb(r,g,b));brush.Freeze();return brush; }

    internal void Update(IReadOnlyList<SessionDiagnosticEvent> events,long currentSession)
    {
        var keep=events.Select(e=>e.Number).ToHashSet();
        var current=events.Where(e=>e.SessionId==currentSession);
        var start=current.LastOrDefault(e=>e.Kind=="Начало");
        var end=current.LastOrDefault(e=>e.Kind=="Окончание");
        history.BeginChange();
        try
        {
            if(!_initialized) { history.Document.Blocks.Clear(); _initialized=true; }
            foreach(long number in _paragraphs.Keys.Where(n=>!keep.Contains(n)).ToArray())
            { history.Document.Blocks.Remove(_paragraphs[number]);_paragraphs.Remove(number); }
            // Reverse insertion also restores the correct order when disabling a filter.
            Paragraph? next=null;
            for(int i=events.Count-1;i>=0;i--)
            {
                var entry=events[i];
                if(!_paragraphs.TryGetValue(entry.Number,out var paragraph))
                {
                    paragraph=new Paragraph(new Run(entry.ToString())) {Margin=new Thickness(0,0,0,3)};
                    if(next==null)history.Document.Blocks.Add(paragraph);
                    else history.Document.Blocks.InsertBefore(next,paragraph);
                    _paragraphs.Add(entry.Number,paragraph);
                }
                Brush color=entry==start?Brushes.Lime:entry==end?BrightRed:
                    entry.Kind=="Начало"?OldGreen:entry.Kind=="Окончание"?OldRed:Brushes.Gainsboro;
                var run=(Run)paragraph.Inlines.FirstInline!;
                if(run.Foreground!=color)run.Foreground=color;
                next=paragraph;
            }
        }
        finally{history.EndChange();}
    }
}
