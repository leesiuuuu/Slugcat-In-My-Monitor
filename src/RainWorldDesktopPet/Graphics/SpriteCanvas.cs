using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RainWorldDesktopPet.Graphics
{
    internal interface ISpriteCanvas
    {
        void Save();
        void Restore();
        void SetTransform(float m11, float m12, float m21, float m22,
            float offsetX, float offsetY);
        void TranslateTransform(float x, float y);
        void RotateTransform(float degrees);
        void ScaleTransform(float x, float y);
        void DrawImage(Bitmap bitmap, PointF[] destination, RectangleF source,
            Color tint, bool dynamicTexture);
        void FillPolygon(Color color, PointF[] points);
        void DrawLines(Color color, float width, PointF[] points);
        void DrawLine(Color color, float width, PointF from, PointF to);
        void FillEllipse(Color color, float x, float y, float width, float height);
        void DrawEllipse(Color color, float strokeWidth, float x, float y,
            float width, float height);
        void DrawPolygon(Color color, float width, PointF[] points);
        void DrawString(string text, Font font, Color color, PointF position);
    }

    internal sealed class GdiSpriteCanvas : ISpriteCanvas
    {
        private readonly Func<Color, ImageAttributes> tintResolver;
        private readonly Stack<GraphicsState> states = new Stack<GraphicsState>(8);
        private System.Drawing.Graphics graphics;

        internal GdiSpriteCanvas(Func<Color, ImageAttributes> tintResolver)
        {
            if (tintResolver == null) throw new ArgumentNullException("tintResolver");
            this.tintResolver = tintResolver;
        }

        internal void Begin(System.Drawing.Graphics target)
        {
            if (target == null) throw new ArgumentNullException("target");
            if (graphics != null || states.Count != 0)
                throw new InvalidOperationException("The GDI canvas is already active.");
            graphics = target;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        }

        internal void End()
        {
            if (states.Count != 0)
                throw new InvalidOperationException("Unbalanced GDI canvas state.");
            graphics = null;
        }

        internal System.Drawing.Graphics Target { get { return RequireGraphics(); } }

        public void Save()
        {
            states.Push(RequireGraphics().Save());
        }

        public void Restore()
        {
            if (states.Count == 0) throw new InvalidOperationException("No canvas state to restore.");
            RequireGraphics().Restore(states.Pop());
        }

        public void SetTransform(float m11, float m12, float m21, float m22,
            float offsetX, float offsetY)
        {
            using (Matrix transform = new Matrix(m11, m12, m21, m22,
                offsetX, offsetY))
                RequireGraphics().Transform = transform;
        }

        public void TranslateTransform(float x, float y)
        {
            RequireGraphics().TranslateTransform(x, y);
        }

        public void RotateTransform(float degrees)
        {
            RequireGraphics().RotateTransform(degrees);
        }

        public void ScaleTransform(float x, float y)
        {
            RequireGraphics().ScaleTransform(x, y);
        }

        public void DrawImage(Bitmap bitmap, PointF[] destination, RectangleF source,
            Color tint, bool dynamicTexture)
        {
            RequireGraphics().DrawImage(bitmap, destination, source,
                GraphicsUnit.Pixel, tintResolver(tint), null, 0);
        }

        public void FillPolygon(Color color, PointF[] points)
        {
            using (Brush brush = new SolidBrush(color))
                RequireGraphics().FillPolygon(brush, points, FillMode.Winding);
        }

        public void DrawLines(Color color, float width, PointF[] points)
        {
            using (Pen pen = CreateRoundPen(color, width))
                RequireGraphics().DrawLines(pen, points);
        }

        public void DrawLine(Color color, float width, PointF from, PointF to)
        {
            using (Pen pen = CreateRoundPen(color, width))
                RequireGraphics().DrawLine(pen, from, to);
        }

        public void FillEllipse(Color color, float x, float y, float width, float height)
        {
            using (Brush brush = new SolidBrush(color))
                RequireGraphics().FillEllipse(brush, x, y, width, height);
        }

        public void DrawEllipse(Color color, float strokeWidth, float x, float y,
            float width, float height)
        {
            using (Pen pen = new Pen(color, strokeWidth))
                RequireGraphics().DrawEllipse(pen, x, y, width, height);
        }

        public void DrawPolygon(Color color, float width, PointF[] points)
        {
            using (Pen pen = new Pen(color, width))
                RequireGraphics().DrawPolygon(pen, points);
        }

        public void DrawString(string text, Font font, Color color, PointF position)
        {
            using (Brush brush = new SolidBrush(color))
                RequireGraphics().DrawString(text, font, brush, position);
        }

        private System.Drawing.Graphics RequireGraphics()
        {
            if (graphics == null) throw new InvalidOperationException("The GDI canvas is not active.");
            return graphics;
        }

        private static Pen CreateRoundPen(Color color, float width)
        {
            Pen pen = new Pen(color, width);
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            return pen;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuPoint
    {
        internal GpuPoint(float x, float y) { X = x; Y = y; }
        internal float X;
        internal float Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuDrawCommand
    {
        internal int Kind;
        internal int TextureId;
        internal int PointOffset;
        internal int PointCount;
        internal float X;
        internal float Y;
        internal float Width;
        internal float Height;
        internal float SourceX;
        internal float SourceY;
        internal float SourceWidth;
        internal float SourceHeight;
        internal float StrokeWidth;
        internal int Color;
    }

    internal enum GpuDrawKind
    {
        FillPolygon = 1,
        StrokePolyline = 2,
        FillEllipse = 3,
        StrokeEllipse = 4,
        Image = 5
    }

    internal sealed class GpuSpriteCanvas : ISpriteCanvas
    {
        private struct AffineTransform
        {
            internal float M11, M12, M21, M22, OffsetX, OffsetY;

            internal static AffineTransform Identity
            {
                get
                {
                    return new AffineTransform { M11 = 1.0f, M22 = 1.0f };
                }
            }

            internal PointF Transform(PointF point)
            {
                return new PointF(point.X * M11 + point.Y * M21 + OffsetX,
                    point.X * M12 + point.Y * M22 + OffsetY);
            }

            internal static AffineTransform Prepend(AffineTransform operation,
                AffineTransform current)
            {
                return new AffineTransform
                {
                    M11 = operation.M11 * current.M11 + operation.M12 * current.M21,
                    M12 = operation.M11 * current.M12 + operation.M12 * current.M22,
                    M21 = operation.M21 * current.M11 + operation.M22 * current.M21,
                    M22 = operation.M21 * current.M12 + operation.M22 * current.M22,
                    OffsetX = operation.OffsetX * current.M11 +
                        operation.OffsetY * current.M21 + current.OffsetX,
                    OffsetY = operation.OffsetX * current.M12 +
                        operation.OffsetY * current.M22 + current.OffsetY
                };
            }
        }

        private readonly DirectCompositionHost host;
        private GpuDrawCommand[] commands = new GpuDrawCommand[512];
        private GpuPoint[] points = new GpuPoint[2048];
        private int commandCount;
        private int pointCount;
        private readonly Stack<AffineTransform> states = new Stack<AffineTransform>(8);
        private AffineTransform transform = AffineTransform.Identity;

        internal GpuSpriteCanvas(DirectCompositionHost host)
        {
            if (host == null) throw new ArgumentNullException("host");
            this.host = host;
        }

        internal void Begin(int slot, Rectangle bounds, Size surfaceSize)
        {
            Slot = slot;
            Bounds = bounds;
            SurfaceSize = surfaceSize;
            commandCount = 0;
            pointCount = 0;
            states.Clear();
            transform = AffineTransform.Identity;
        }

        internal GpuDrawCommand[] Commands { get { return commands; } }
        internal GpuPoint[] Points { get { return points; } }
        internal int CommandCount { get { return commandCount; } }
        internal int PointCount { get { return pointCount; } }
        internal int Slot { get; private set; }
        internal Rectangle Bounds { get; private set; }
        internal Size SurfaceSize { get; private set; }

        public void Save() { states.Push(transform); }

        public void Restore()
        {
            if (states.Count == 0) throw new InvalidOperationException("No canvas state to restore.");
            transform = states.Pop();
        }

        public void SetTransform(float m11, float m12, float m21, float m22,
            float offsetX, float offsetY)
        {
            transform = new AffineTransform
            {
                M11 = m11, M12 = m12, M21 = m21, M22 = m22,
                OffsetX = offsetX, OffsetY = offsetY
            };
        }

        public void TranslateTransform(float x, float y)
        {
            AffineTransform operation = AffineTransform.Identity;
            operation.OffsetX = x;
            operation.OffsetY = y;
            transform = AffineTransform.Prepend(operation, transform);
        }

        public void RotateTransform(float degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            float cosine = (float)Math.Cos(radians);
            float sine = (float)Math.Sin(radians);
            AffineTransform operation = new AffineTransform
            {
                M11 = cosine, M12 = sine, M21 = -sine, M22 = cosine
            };
            transform = AffineTransform.Prepend(operation, transform);
        }

        public void ScaleTransform(float x, float y)
        {
            AffineTransform operation = new AffineTransform { M11 = x, M22 = y };
            transform = AffineTransform.Prepend(operation, transform);
        }

        public void DrawImage(Bitmap bitmap, PointF[] destination, RectangleF source,
            Color tint, bool dynamicTexture)
        {
            if (bitmap == null || destination == null || destination.Length < 3) return;
            int textureId = host.GetGpuTexture(bitmap, dynamicTexture);
            int pointOffset = AddPoints(destination, 3);
            AddCommand(new GpuDrawCommand
            {
                Kind = (int)GpuDrawKind.Image,
                TextureId = textureId,
                PointOffset = pointOffset,
                PointCount = 3,
                SourceX = source.X,
                SourceY = source.Y,
                SourceWidth = source.Width,
                SourceHeight = source.Height,
                Color = tint.ToArgb()
            });
        }

        public void FillPolygon(Color color, PointF[] polygon)
        {
            if (polygon == null || polygon.Length < 3) return;
            AddPointCommand(GpuDrawKind.FillPolygon, color, 0.0f, polygon,
                polygon.Length);
        }

        public void DrawLines(Color color, float width, PointF[] linePoints)
        {
            if (linePoints == null || linePoints.Length < 2) return;
            AddPointCommand(GpuDrawKind.StrokePolyline, color,
                TransformStrokeWidth(width), linePoints, linePoints.Length);
        }

        public void DrawLine(Color color, float width, PointF from, PointF to)
        {
            EnsurePointCapacity(pointCount + 2);
            int offset = pointCount;
            PointF transformed = transform.Transform(from);
            points[pointCount++] = new GpuPoint(transformed.X, transformed.Y);
            transformed = transform.Transform(to);
            points[pointCount++] = new GpuPoint(transformed.X, transformed.Y);
            AddCommand(new GpuDrawCommand
            {
                Kind = (int)GpuDrawKind.StrokePolyline,
                PointOffset = offset,
                PointCount = 2,
                StrokeWidth = TransformStrokeWidth(width),
                Color = color.ToArgb()
            });
        }

        public void FillEllipse(Color color, float x, float y, float width, float height)
        {
            AddEllipseCommand(GpuDrawKind.FillEllipse, color, 0.0f,
                x, y, width, height);
        }

        public void DrawEllipse(Color color, float strokeWidth, float x, float y,
            float width, float height)
        {
            AddEllipseCommand(GpuDrawKind.StrokeEllipse, color,
                TransformStrokeWidth(strokeWidth), x, y, width, height);
        }

        public void DrawPolygon(Color color, float width, PointF[] polygon)
        {
            if (polygon == null || polygon.Length < 2) return;
            int offset = AddPoints(polygon, polygon.Length);
            EnsurePointCapacity(pointCount + 1);
            PointF first = transform.Transform(polygon[0]);
            points[pointCount++] = new GpuPoint(first.X, first.Y);
            AddCommand(new GpuDrawCommand
            {
                Kind = (int)GpuDrawKind.StrokePolyline,
                PointOffset = offset,
                PointCount = polygon.Length + 1,
                StrokeWidth = TransformStrokeWidth(width),
                Color = color.ToArgb()
            });
        }

        public void DrawString(string text, Font font, Color color, PointF position)
        {
            throw new NotSupportedException("Debug text uses the GDI fallback renderer.");
        }

        private void AddPointCommand(GpuDrawKind kind, Color color, float width,
            PointF[] values, int count)
        {
            int offset = AddPoints(values, count);
            AddCommand(new GpuDrawCommand
            {
                Kind = (int)kind,
                PointOffset = offset,
                PointCount = count,
                StrokeWidth = width,
                Color = color.ToArgb()
            });
        }

        private int AddPoints(PointF[] values, int count)
        {
            int offset = pointCount;
            EnsurePointCapacity(pointCount + count);
            for (int i = 0; i < count; i++)
            {
                PointF transformed = transform.Transform(values[i]);
                points[pointCount++] = new GpuPoint(transformed.X, transformed.Y);
            }
            return offset;
        }

        private void AddEllipseCommand(GpuDrawKind kind, Color color,
            float strokeWidth, float x, float y, float width, float height)
        {
            PointF center = transform.Transform(new PointF(x + width * 0.5f,
                y + height * 0.5f));
            float radiusX = width * 0.5f * AxisScale(transform.M11, transform.M12);
            float radiusY = height * 0.5f * AxisScale(transform.M21, transform.M22);
            AddCommand(new GpuDrawCommand
            {
                Kind = (int)kind,
                X = center.X,
                Y = center.Y,
                Width = Math.Abs(radiusX),
                Height = Math.Abs(radiusY),
                StrokeWidth = strokeWidth,
                Color = color.ToArgb()
            });
        }

        private void AddCommand(GpuDrawCommand command)
        {
            EnsureCommandCapacity(commandCount + 1);
            commands[commandCount++] = command;
        }

        private void EnsureCommandCapacity(int required)
        {
            if (required <= commands.Length) return;
            int capacity = Math.Max(required, commands.Length * 2);
            Array.Resize(ref commands, capacity);
        }

        private void EnsurePointCapacity(int required)
        {
            if (required <= points.Length) return;
            int capacity = Math.Max(required, points.Length * 2);
            Array.Resize(ref points, capacity);
        }

        private float TransformStrokeWidth(float width)
        {
            float xScale = AxisScale(transform.M11, transform.M12);
            float yScale = AxisScale(transform.M21, transform.M22);
            return width * (xScale + yScale) * 0.5f;
        }

        private static float AxisScale(float x, float y)
        {
            return (float)Math.Sqrt(x * x + y * y);
        }
    }
}
