using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PlotterNew.Core.Models;
using PlotterNew.Core.Services;
using PlotterNew.UI.Interactions;
using PlotterNew.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PlotterNew.UI.Views
{
    public sealed class Canvas2D : Control
    {
        public static readonly StyledProperty<PlotModel> ModelProperty =
            AvaloniaProperty.Register<Canvas2D, PlotModel>(nameof(Model));

        public PlotModel Model
        {
            get => GetValue(ModelProperty);
            set => SetValue(ModelProperty, value);
        }

        public override void Render(DrawingContext ctx)
        {
            base.Render(ctx);

            if (App.MainVM.IsDataFeedRunning)
            {
                ctx.FillRectangle(Brushes.Black, Bounds);
            }
            else
            {
                ctx.FillRectangle(Brushes.Red, Bounds);
            }
        }

        //public override void Render(DrawingContext ctx)
        //{
        //    base.Render(ctx);
        //    ctx.FillRectangle(Brushes.Black, Bounds);
        //    if (Model is null) return;
        //    ctx.DrawLine(new Pen(Brushes.White), new Point(0,0), new Point(100, 100));

        //    var (i0, i1) = Model.GetVisibleRangeIndices();
        //    foreach (var s in Model.SeriesList)
        //    {
        //        if (s.Count == 0 || i0 >= s.Count) continue;
        //        var geo = new StreamGeometry();
        //        using var g = geo.Open();

        //        // choose source indices (decimated or raw)
        //        IEnumerable<int> indices = s.DecimatedIndices.Count > 0
        //            ? s.DecimatedIndices
        //            : Enumerable.Range(i0, Math.Max(0, i1 - i0 + 1));

        //        bool first = true;
        //        foreach (var i in indices)
        //        {
        //            //var p = DataToScreen(Model.TimeSeries[i], s.CalcTransformedY(i), Model);
        //            var p = new Point(Model.TimeSeries[i], s.CalcTransformedY(i));
        //            if (first) { g.BeginFigure(p, false); first = false; }
        //            else g.LineTo(p);
        //        }
        //        if (!first) g.EndFigure(false);

        //        ctx.DrawGeometry(null, s.Pen, geo);
        //    }
        //}

        //private Point DataToScreen(double x, double y, PlotModel m)
        //{
        //    double sx = Bounds.X + (x - m.XMin) / (m.XMax - m.XMin) * Bounds.Width;
        //    double sy = Bounds.Y + (m.YMax - y) / (m.YMax - m.YMin) * Bounds.Height;
        //    return new Point(sx, sy);
        //}

        // (Pointer handlers omitted here for brevity; you already have them wired to IPlotInteractor.)
    }
}
