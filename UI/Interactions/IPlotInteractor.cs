namespace PlotterNew.UI.Interactions
{
    public interface IPlotInteractor
    {
        void OnPanStart(Avalonia.Point anchor);
        void OnPanDelta(Avalonia.Point delta);
        void OnPanEnd();
    }
}
