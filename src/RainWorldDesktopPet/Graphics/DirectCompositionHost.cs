using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RainWorldDesktopPet.Graphics
{
    public sealed class DirectCompositionHost : IDisposable
    {
        private const int MaximumSurfaces = 8;
        private const int EffectSurfaceQuantum = 128;
        private const int SurfaceShrinkDelayMilliseconds = 8000;
        private const int SurfaceReleaseDelayMilliseconds = 10000;
        private const double SurfaceShrinkThreshold = 0.70;
        private const double SurfaceShrinkHeadroom = 1.20;
        private readonly CompositionSurface[] surfaces = new CompositionSurface[MaximumSurfaces];
        private readonly Size[] gpuSurfaceSizes = new Size[MaximumSurfaces];
        private readonly Size[] effectSurfaceSizes = new Size[MaximumSurfaces];
        private readonly uint[] surfaceShrinkSince = new uint[MaximumSurfaces];
        private readonly uint[] effectShrinkSince = new uint[MaximumSurfaces];
        private readonly uint[] surfaceInactiveSince = new uint[MaximumSurfaces];
        private readonly uint[] effectInactiveSince = new uint[MaximumSurfaces];
        private IntPtr nativeRenderer;
        private Rectangle desktopBounds;
        private uint activeEffectMask;
        private bool disposed;
        private readonly GpuSpriteCanvas gpuCanvas;
        private readonly Dictionary<Bitmap, GpuTextureRecord> gpuTextures =
            new Dictionary<Bitmap, GpuTextureRecord>(BitmapReferenceComparer.Instance);

        private sealed class GpuTextureRecord
        {
            internal int Id;
            internal int Width;
            internal int Height;
        }

        private sealed class BitmapReferenceComparer : IEqualityComparer<Bitmap>
        {
            internal static readonly BitmapReferenceComparer Instance =
                new BitmapReferenceComparer();
            public bool Equals(Bitmap left, Bitmap right)
            {
                return ReferenceEquals(left, right);
            }
            public int GetHashCode(Bitmap value)
            {
                return RuntimeHelpers.GetHashCode(value);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GpuSmokeEffect
        {
            public float CenterX, CenterY, Rotation, BackSize, FrontSize;
            public float BackRed, BackGreen, BackBlue, BackAlpha;
            public float FrontRed, FrontGreen, FrontBlue, FrontAlpha;
            public float Seed;
        }

        public DirectCompositionHost(IntPtr windowHandle, Rectangle desktopBounds)
        {
            this.desktopBounds = desktopBounds;
            int result = NativeMethods.Create(windowHandle, out nativeRenderer);
            ThrowIfFailed(result, "Could not initialize DirectComposition");
            gpuCanvas = new GpuSpriteCanvas(this);
        }

        public void SetDesktopBounds(Rectangle bounds)
        {
            desktopBounds = bounds;
        }

        public CompositionSurface PrepareSurface(int slot, Rectangle bounds)
        {
            if (slot < 0 || slot >= MaximumSurfaces) throw new ArgumentOutOfRangeException("slot");
            CompositionSurface surface = surfaces[slot];
            Size requiredSize = bounds.Size;
            if (requiredSize.Width < 1 || requiredSize.Height < 1)
                throw new ArgumentOutOfRangeException("bounds");

            if (surface == null)
            {
                surface = new CompositionSurface(requiredSize.Width, requiredSize.Height);
                surfaces[slot] = surface;
                surfaceShrinkSince[slot] = 0;
            }
            else
            {
                Size currentSize = new Size(surface.Width, surface.Height);
                Size reusableSize = SelectReusableSurfaceSize(currentSize, requiredSize);
                bool mustGrow = reusableSize.Width > currentSize.Width ||
                    reusableSize.Height > currentSize.Height;
                if (mustGrow)
                {
                    surface.Dispose();
                    surface = new CompositionSurface(reusableSize.Width, reusableSize.Height);
                    surfaces[slot] = surface;
                    surfaceShrinkSince[slot] = 0;
                }
                else if (ShouldShrinkSurface(currentSize, requiredSize))
                {
                    uint now = CurrentTick();
                    if (surfaceShrinkSince[slot] == 0)
                    {
                        surfaceShrinkSince[slot] = now;
                    }
                    else if (HasElapsed(surfaceShrinkSince[slot], now,
                        SurfaceShrinkDelayMilliseconds))
                    {
                        Size shrinkSize = SelectShrinkSurfaceSize(currentSize, requiredSize);
                        if (shrinkSize.Width < currentSize.Width ||
                            shrinkSize.Height < currentSize.Height)
                        {
                            surface.Dispose();
                            surface = new CompositionSurface(shrinkSize.Width, shrinkSize.Height);
                            surfaces[slot] = surface;
                        }
                        surfaceShrinkSince[slot] = 0;
                    }
                }
                else
                {
                    surfaceShrinkSince[slot] = 0;
                }
            }

            surfaceInactiveSince[slot] = 0;
            gpuSurfaceSizes[slot] = Size.Empty;
            int centerX = bounds.Left + bounds.Width / 2;
            int centerY = bounds.Top + bounds.Height / 2;
            surface.Bounds = new Rectangle(centerX - surface.Width / 2,
                centerY - surface.Height / 2, surface.Width, surface.Height);
            return surface;
        }

        internal GpuSpriteCanvas PrepareGpuSurface(int slot, Rectangle bounds)
        {
            if (slot < 0 || slot >= MaximumSurfaces)
                throw new ArgumentOutOfRangeException("slot");
            if (bounds.Width < 1 || bounds.Height < 1)
                throw new ArgumentOutOfRangeException("bounds");

            Size required = bounds.Size;
            Size current = gpuSurfaceSizes[slot];
            Size reusable = current.IsEmpty ? required :
                SelectReusableSurfaceSize(current, required);
            bool mustGrow = current.IsEmpty || reusable.Width > current.Width ||
                reusable.Height > current.Height;
            if (mustGrow)
            {
                surfaceShrinkSince[slot] = 0;
            }
            else if (ShouldShrinkSurface(current, required))
            {
                uint now = CurrentTick();
                if (surfaceShrinkSince[slot] == 0)
                {
                    surfaceShrinkSince[slot] = now;
                }
                else if (HasElapsed(surfaceShrinkSince[slot], now,
                    SurfaceShrinkDelayMilliseconds))
                {
                    reusable = SelectShrinkSurfaceSize(current, required);
                    surfaceShrinkSince[slot] = 0;
                }
            }
            else
            {
                surfaceShrinkSince[slot] = 0;
            }

            gpuSurfaceSizes[slot] = reusable;
            if (surfaces[slot] != null)
            {
                surfaces[slot].Dispose();
                surfaces[slot] = null;
            }
            surfaceInactiveSince[slot] = 0;
            int centerX = bounds.Left + bounds.Width / 2;
            int centerY = bounds.Top + bounds.Height / 2;
            Rectangle actualBounds = new Rectangle(centerX - reusable.Width / 2,
                centerY - reusable.Height / 2, reusable.Width, reusable.Height);
            gpuCanvas.Begin(slot, actualBounds, reusable);
            return gpuCanvas;
        }

        internal void PresentGpu(GpuSpriteCanvas canvas)
        {
            if (canvas == null) throw new ArgumentNullException("canvas");
            GpuDrawCommand[] commands = canvas.Commands;
            GpuPoint[] points = canvas.Points;
            int result = NativeMethods.PresentGpu(nativeRenderer, canvas.Slot,
                commands, (uint)canvas.CommandCount, points,
                (uint)canvas.PointCount, (uint)canvas.SurfaceSize.Width,
                (uint)canvas.SurfaceSize.Height,
                canvas.Bounds.X - desktopBounds.X,
                canvas.Bounds.Y - desktopBounds.Y);
            ThrowIfFailed(result, "Could not present GPU sprite commands");
        }

        internal int GetGpuTexture(Bitmap bitmap, bool dynamicTexture)
        {
            if (bitmap == null) throw new ArgumentNullException("bitmap");
            GpuTextureRecord record;
            if (!gpuTextures.TryGetValue(bitmap, out record))
            {
                record = new GpuTextureRecord();
                UploadGpuTexture(bitmap, record, false);
                gpuTextures.Add(bitmap, record);
            }
            else if (dynamicTexture)
            {
                UploadGpuTexture(bitmap, record, true);
            }
            return record.Id;
        }

        private void UploadGpuTexture(Bitmap bitmap, GpuTextureRecord record,
            bool update)
        {
            Bitmap converted = null;
            Bitmap source = bitmap;
            if (bitmap.PixelFormat != PixelFormat.Format32bppPArgb)
            {
                converted = new Bitmap(bitmap.Width, bitmap.Height,
                    PixelFormat.Format32bppPArgb);
                using (System.Drawing.Graphics drawing =
                    System.Drawing.Graphics.FromImage(converted))
                {
                    drawing.CompositingMode =
                        System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    drawing.DrawImageUnscaled(bitmap, 0, 0);
                }
                source = converted;
            }

            BitmapData data = source.LockBits(new Rectangle(0, 0,
                source.Width, source.Height), ImageLockMode.ReadOnly,
                PixelFormat.Format32bppPArgb);
            try
            {
                int result;
                if (update && record.Id != 0 && record.Width == source.Width &&
                    record.Height == source.Height)
                {
                    result = NativeMethods.UpdateGpuTexture(nativeRenderer,
                        record.Id, data.Scan0, (uint)source.Width,
                        (uint)source.Height, (uint)Math.Abs(data.Stride));
                }
                else
                {
                    int textureId;
                    result = NativeMethods.RegisterGpuTexture(nativeRenderer,
                        data.Scan0, (uint)source.Width, (uint)source.Height,
                        (uint)Math.Abs(data.Stride), out textureId);
                    if (result >= 0)
                    {
                        record.Id = textureId;
                        record.Width = source.Width;
                        record.Height = source.Height;
                    }
                }
                ThrowIfFailed(result, update ? "Could not update a GPU sprite texture" :
                    "Could not register a GPU sprite texture");
            }
            finally
            {
                source.UnlockBits(data);
                if (converted != null) converted.Dispose();
            }
        }

        public static Size SelectReusableSurfaceSize(Size current, Size required)
        {
            if (required.Width < 1 || required.Height < 1)
                throw new ArgumentOutOfRangeException("required");
            return new Size(Math.Max(current.Width, required.Width),
                Math.Max(current.Height, required.Height));
        }

        private static bool ShouldShrinkSurface(Size current, Size required)
        {
            if (current.Width <= 0 || current.Height <= 0) return false;
            return required.Width <= current.Width * SurfaceShrinkThreshold ||
                required.Height <= current.Height * SurfaceShrinkThreshold;
        }

        private static Size SelectShrinkSurfaceSize(Size current, Size required)
        {
            int targetWidth = RoundEffectSize((int)Math.Ceiling(
                required.Width * SurfaceShrinkHeadroom));
            int targetHeight = RoundEffectSize((int)Math.Ceiling(
                required.Height * SurfaceShrinkHeadroom));
            targetWidth = Math.Max(required.Width, Math.Min(current.Width, targetWidth));
            targetHeight = Math.Max(required.Height, Math.Min(current.Height, targetHeight));
            return new Size(targetWidth, targetHeight);
        }

        private static uint CurrentTick()
        {
            return unchecked((uint)Environment.TickCount);
        }

        private static bool HasElapsed(uint start, uint now, int milliseconds)
        {
            return start != 0 && unchecked(now - start) >= unchecked((uint)milliseconds);
        }

        public void ResetSurfaces()
        {
            if (disposed) return;
            activeEffectMask = 0;
            int result = NativeMethods.Commit(nativeRenderer, 0, 0);
            ThrowIfFailed(result, "Could not reset DirectComposition surfaces");
            for (int i = 0; i < surfaces.Length; i++)
            {
                if (surfaces[i] != null) surfaces[i].Dispose();
                surfaces[i] = null;
                gpuSurfaceSizes[i] = Size.Empty;
                effectSurfaceSizes[i] = Size.Empty;
                surfaceShrinkSince[i] = 0;
                effectShrinkSince[i] = 0;
                surfaceInactiveSince[i] = 0;
                effectInactiveSince[i] = 0;
            }
        }

        public void BeginEffectFrame()
        {
            activeEffectMask = 0;
        }

        public Rectangle PrepareEffectBounds(int slot, RectangleF requiredBounds)
        {
            if (slot < 0 || slot >= MaximumSurfaces)
                throw new ArgumentOutOfRangeException("slot");
            int requiredWidth = RoundEffectSize((int)Math.Ceiling(requiredBounds.Width) + 8);
            int requiredHeight = RoundEffectSize((int)Math.Ceiling(requiredBounds.Height) + 8);
            Size required = new Size(requiredWidth, requiredHeight);
            Size current = effectSurfaceSizes[slot];
            Size reusable;
            if (current.IsEmpty)
            {
                reusable = required;
                effectShrinkSince[slot] = 0;
            }
            else
            {
                reusable = SelectReusableSurfaceSize(current, required);
                bool mustGrow = reusable.Width > current.Width || reusable.Height > current.Height;
                if (mustGrow)
                {
                    effectShrinkSince[slot] = 0;
                }
                else if (ShouldShrinkSurface(current, required))
                {
                    uint now = CurrentTick();
                    if (effectShrinkSince[slot] == 0)
                    {
                        effectShrinkSince[slot] = now;
                    }
                    else if (HasElapsed(effectShrinkSince[slot], now,
                        SurfaceShrinkDelayMilliseconds))
                    {
                        reusable = SelectShrinkSurfaceSize(current, required);
                        effectShrinkSince[slot] = 0;
                    }
                }
                else
                {
                    effectShrinkSince[slot] = 0;
                }
            }
            effectSurfaceSizes[slot] = reusable;
            effectInactiveSince[slot] = 0;
            int centerX = (int)Math.Round(requiredBounds.Left + requiredBounds.Width * 0.5f);
            int centerY = (int)Math.Round(requiredBounds.Top + requiredBounds.Height * 0.5f);
            return new Rectangle(centerX - reusable.Width / 2,
                centerY - reusable.Height / 2, reusable.Width, reusable.Height);
        }

        private static int RoundEffectSize(int value)
        {
            return ((Math.Max(1, value) + EffectSurfaceQuantum - 1) /
                EffectSurfaceQuantum) * EffectSurfaceQuantum;
        }

        public void PresentEffects(int slot, GpuSmokeEffect[] effects, int count,
            Rectangle bounds)
        {
            if (slot < 0 || slot >= MaximumSurfaces)
                throw new ArgumentOutOfRangeException("slot");
            if (effects == null) throw new ArgumentNullException("effects");
            if (count < 0 || count > effects.Length)
                throw new ArgumentOutOfRangeException("count");
            if (count == 0) return;
            int result = NativeMethods.PresentEffects(nativeRenderer, slot, effects,
                (uint)count, (uint)bounds.Width, (uint)bounds.Height,
                bounds.X - desktopBounds.X, bounds.Y - desktopBounds.Y);
            ThrowIfFailed(result, "Could not present GPU effects");
            activeEffectMask |= 1u << slot;
            effectInactiveSince[slot] = 0;
        }

        public void Present(int slot)
        {
            CompositionSurface surface = surfaces[slot];
            if (surface == null) throw new InvalidOperationException("The composition surface was not prepared.");
            surface.Graphics.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
            BitmapData data = surface.Bitmap.LockBits(new Rectangle(0, 0, surface.Width, surface.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                int result = NativeMethods.Present(nativeRenderer, slot, data.Scan0,
                    (uint)surface.Width, (uint)surface.Height, (uint)Math.Abs(data.Stride),
                    surface.Bounds.X - desktopBounds.X, surface.Bounds.Y - desktopBounds.Y);
                ThrowIfFailed(result, "Could not present a DirectComposition surface");
            }
            finally
            {
                surface.Bitmap.UnlockBits(data);
            }
        }

        public void Commit(int activeSurfaceCount)
        {
            uint activeMask = activeSurfaceCount <= 0 ? 0u : (1u << activeSurfaceCount) - 1u;
            uint effectMask = activeEffectMask;
            int result = NativeMethods.Commit(nativeRenderer, activeMask, effectMask);
            ThrowIfFailed(result, "Could not commit the DirectComposition frame");
            ReleaseInactiveManagedSurfaces(activeMask, effectMask);
            activeEffectMask = 0;
        }

        private void ReleaseInactiveManagedSurfaces(uint activeMask, uint effectMask)
        {
            uint now = CurrentTick();
            for (int i = 0; i < MaximumSurfaces; i++)
            {
                uint bit = 1u << i;
                if ((activeMask & bit) != 0)
                {
                    surfaceInactiveSince[i] = 0;
                }
                else if (surfaces[i] != null || !gpuSurfaceSizes[i].IsEmpty)
                {
                    if (surfaceInactiveSince[i] == 0)
                    {
                        surfaceInactiveSince[i] = now;
                    }
                    else if (HasElapsed(surfaceInactiveSince[i], now,
                        SurfaceReleaseDelayMilliseconds))
                    {
                        if (surfaces[i] != null) surfaces[i].Dispose();
                        surfaces[i] = null;
                        gpuSurfaceSizes[i] = Size.Empty;
                        surfaceShrinkSince[i] = 0;
                        surfaceInactiveSince[i] = 0;
                    }
                }

                if ((effectMask & bit) != 0)
                {
                    effectInactiveSince[i] = 0;
                }
                else if (!effectSurfaceSizes[i].IsEmpty)
                {
                    if (effectInactiveSince[i] == 0)
                    {
                        effectInactiveSince[i] = now;
                    }
                    else if (HasElapsed(effectInactiveSince[i], now,
                        SurfaceReleaseDelayMilliseconds))
                    {
                        effectSurfaceSizes[i] = Size.Empty;
                        effectShrinkSince[i] = 0;
                        effectInactiveSince[i] = 0;
                    }
                }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (nativeRenderer != IntPtr.Zero)
            {
                NativeMethods.Destroy(nativeRenderer);
                nativeRenderer = IntPtr.Zero;
            }
            gpuTextures.Clear();
            for (int i = 0; i < surfaces.Length; i++)
            {
                if (surfaces[i] != null) surfaces[i].Dispose();
                surfaces[i] = null;
                gpuSurfaceSizes[i] = Size.Empty;
                effectSurfaceSizes[i] = Size.Empty;
                surfaceShrinkSince[i] = 0;
                effectShrinkSince[i] = 0;
                surfaceInactiveSince[i] = 0;
                effectInactiveSince[i] = 0;
            }
        }

        private static void ThrowIfFailed(int result, string operation)
        {
            if (result >= 0) return;
            Exception exception = Marshal.GetExceptionForHR(result);
            throw new InvalidOperationException(operation + " (HRESULT 0x" +
                result.ToString("X8") + ").", exception);
        }

        public sealed class CompositionSurface : IDisposable
        {
            private readonly Bitmap bitmap;
            private readonly System.Drawing.Graphics graphics;

            internal CompositionSurface(int width, int height)
            {
                bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                graphics = System.Drawing.Graphics.FromImage(bitmap);
            }

            public int Width { get { return bitmap.Width; } }
            public int Height { get { return bitmap.Height; } }
            public Rectangle Bounds { get; internal set; }
            public System.Drawing.Graphics Graphics { get { return graphics; } }
            internal Bitmap Bitmap { get { return bitmap; } }

            public void Dispose()
            {
                graphics.Dispose();
                bitmap.Dispose();
            }
        }

        private static class NativeMethods
        {
            private const string Library = "SlugcatInMyMonitor.DirectComposition.dll";

            [DllImport(Library, EntryPoint = "SlugcatDCompCreate", CallingConvention = CallingConvention.StdCall)]
            internal static extern int Create(IntPtr windowHandle, out IntPtr renderer);

            [DllImport(Library, EntryPoint = "SlugcatDCompPresent", CallingConvention = CallingConvention.StdCall)]
            internal static extern int Present(IntPtr renderer, int slot, IntPtr pixels,
                uint width, uint height, uint stride, float x, float y);

            [DllImport(Library, EntryPoint = "SlugcatDCompRegisterGpuTexture",
                CallingConvention = CallingConvention.StdCall)]
            internal static extern int RegisterGpuTexture(IntPtr renderer,
                IntPtr pixels, uint width, uint height, uint stride,
                out int textureId);

            [DllImport(Library, EntryPoint = "SlugcatDCompUpdateGpuTexture",
                CallingConvention = CallingConvention.StdCall)]
            internal static extern int UpdateGpuTexture(IntPtr renderer,
                int textureId, IntPtr pixels, uint width, uint height, uint stride);

            [DllImport(Library, EntryPoint = "SlugcatDCompPresentGpu",
                CallingConvention = CallingConvention.StdCall)]
            internal static extern int PresentGpu(IntPtr renderer, int slot,
                [In] GpuDrawCommand[] commands, uint commandCount,
                [In] GpuPoint[] points, uint pointCount, uint width, uint height,
                float x, float y);

            [DllImport(Library, EntryPoint = "SlugcatDCompPresentEffects", CallingConvention = CallingConvention.StdCall)]
            internal static extern int PresentEffects(IntPtr renderer, int slot,
                [In] GpuSmokeEffect[] effects, uint count, uint width, uint height,
                float x, float y);

            [DllImport(Library, EntryPoint = "SlugcatDCompCommit", CallingConvention = CallingConvention.StdCall)]
            internal static extern int Commit(IntPtr renderer, uint activeMask,
                uint activeEffectMask);

            [DllImport(Library, EntryPoint = "SlugcatDCompDestroy", CallingConvention = CallingConvention.StdCall)]
            internal static extern void Destroy(IntPtr renderer);
        }
    }
}
