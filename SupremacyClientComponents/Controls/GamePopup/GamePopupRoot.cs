using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Supremacy.Client.Controls
{
    [TemplatePart(Name = "PART_MainContainer", Type = typeof(DockPanel))]
    [TemplatePart(Name = "PART_Arrow", Type = typeof(Grid))]
    internal sealed class GamePopupRoot : FrameworkElement
    {
        private static readonly Thickness ZeroThickness = new Thickness(0);

        private readonly GamePopup _popup;

        private AdornerDecorator _adornerDecorator;
        private Decorator _transformDecorator;

        private double _dpiXfactor = 1d;
        private double _dpiYfactor = 1d;
        private Grid _arrow;
        private DockPanel _mainContainer;
        private Point _relativeToTargetPoint;

        static GamePopupRoot()
        {
            SnapsToDevicePixelsProperty.OverrideMetadata(
                typeof(GamePopupRoot),
                new FrameworkPropertyMetadata(true));

            CommandManager.RegisterClassCommandBinding(
                typeof(GamePopupRoot),
                new CommandBinding(
                    GamePopup.ClosePopupCommand,
                    (sender, args) =>
                    {
                        GamePopupRoot popup = (GamePopupRoot)sender;
                        popup._popup.IsOpen = false;
                        args.Handled = true;
                    },
                    (sender, args) =>
                    {
                        GamePopupRoot popup = (GamePopupRoot)sender;
                        args.CanExecute = popup._popup.IsOpen;
                        args.Handled = true;
                    }));
        }

        internal GamePopupRoot(GamePopup popup)
        {
            _popup = popup;
            Initialize();
        }

        internal GamePopup Popup => _popup;

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            _popup.Reposition();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _popup.IsOpen)
            {
                _popup.IsOpen = false;
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            _transformDecorator.Arrange(new Rect(arrangeSize));
            return arrangeSize;
        }

        protected override Visual GetVisualChild(int index)
        {
            return _transformDecorator;
        }

        private void Initialize()
        {
            _transformDecorator = new Decorator();

            AddVisualChild(_transformDecorator);

            _adornerDecorator = new NonLogicalAdornerDecorator();
            _transformDecorator.Child = _adornerDecorator;
        }

        protected override DependencyObject GetUIParentCore()
        {
            return _popup ?? base.GetUIParentCore();
        }

        protected override Size MeasureOverride(Size constraint)
        {
            _transformDecorator.Measure(
                new Size(
                    double.PositiveInfinity,
                    double.PositiveInfinity));

            Size desiredSize = _transformDecorator.DesiredSize;

            return desiredSize;
        }

        internal void SetupFadeAnimation(Duration duration, bool visible)
        {
            DoubleAnimation animation = new DoubleAnimation(
                visible ? 0.0 : 1.0,
                visible ? 1.0 : 0.0,
                duration,
                FillBehavior.HoldEnd);

            BeginAnimation(OpacityProperty, animation);
        }

        internal void SetupLayoutBindings(GamePopup popup)
        {
            _adornerDecorator.SetBinding(
                WidthProperty,
                new Binding
                {
                    Mode = BindingMode.OneWay,
                    Source = popup,
                    Path = new PropertyPath(WidthProperty)
                });

            _adornerDecorator.SetBinding(
                HeightProperty,
                new Binding
                {
                    Mode = BindingMode.OneWay,
                    Source = popup,
                    Path = new PropertyPath(HeightProperty)
                });

            _adornerDecorator.SetBinding(
                MinWidthProperty,
                new Binding
                {
                    Mode = BindingMode.OneWay,
                    Source = popup,
                    Path = new PropertyPath(MinWidthProperty)
                });

            _adornerDecorator.SetBinding(
                MinHeightProperty,
                new Binding
                {
                    Mode = BindingMode.OneWay,
                    Source = popup,
                    Path = new PropertyPath(MinHeightProperty)
                });

            _adornerDecorator.SetBinding(
                MaxWidthProperty,
                new Binding
                {
                    Mode = BindingMode.OneWay,
                    Source = popup,
                    Path = new PropertyPath(MaxWidthProperty)
                });

            _adornerDecorator.SetBinding(
                MaxHeightProperty,
                new Binding
                {
                    Mode = BindingMode.OneWay,
                    Source = popup,
                    Path = new PropertyPath(MaxHeightProperty),
                });

            _adornerDecorator.SetBinding(
                ClipToBoundsProperty,
                new Binding
                {
                    Mode = BindingMode.OneWay,
                    Source = popup,
                    Path = new PropertyPath(ClipToBoundsProperty),
                });
        }

        internal void SetupTranslateAnimations(PopupAnimation animationType, Duration duration, bool animateFromRight, bool animateFromBottom)
        {
            UIElement child = Child;
            if (child == null)
                return;

            TranslateTransform renderTransform = _adornerDecorator.RenderTransform as TranslateTransform;
            if (renderTransform == null)
            {
                renderTransform = new TranslateTransform();
                _adornerDecorator.RenderTransform = renderTransform;
            }

            if (animationType == PopupAnimation.Scroll)
            {
                FlowDirection direction = (FlowDirection)child.GetValue(FlowDirectionProperty);
                FlowDirection flowDirection = FlowDirection;

                if (direction != flowDirection)
                    animateFromRight = !animateFromRight;

                double width = _adornerDecorator.RenderSize.Width;
                DoubleAnimation horizontalAnimation = new DoubleAnimation(
                    animateFromRight ? width : -width,
                    0.0,
                    duration,
                    FillBehavior.Stop);

                renderTransform.BeginAnimation(TranslateTransform.XProperty, horizontalAnimation);
            }

            double height = _adornerDecorator.RenderSize.Height;
            DoubleAnimation verticalAnimation = new DoubleAnimation(
                animateFromBottom ? height : -height,
                0.0,
                duration,
                FillBehavior.Stop);

            renderTransform.BeginAnimation(TranslateTransform.YProperty, verticalAnimation);
        }

        internal void StopAnimations()
        {
            BeginAnimation(OpacityProperty, null);

            TranslateTransform renderTransform = _adornerDecorator.RenderTransform as TranslateTransform;
            if (renderTransform == null)
                return;

            renderTransform.BeginAnimation(TranslateTransform.XProperty, null);
            renderTransform.BeginAnimation(TranslateTransform.YProperty, null);
        }

        internal Vector AnimationOffset
        {
            get
            {
                TranslateTransform renderTransform = _adornerDecorator.RenderTransform as TranslateTransform;
                if (renderTransform != null)
                    return new Vector(renderTransform.X, renderTransform.Y);
                return new Vector();
            }
        }

        internal UIElement Child
        {
            get { return _adornerDecorator.Child; }
            set { _adornerDecorator.Child = value; }
        }

        internal Transform Transform
        {
            set { _transformDecorator.LayoutTransform = value; }
        }

        protected override int VisualChildrenCount => 1;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _arrow = GetTemplateChild("PART_Arrow") as Grid;
            _mainContainer = GetTemplateChild("PART_MainContainer") as DockPanel;
        }

        private Thickness MainContainerMargin => _mainContainer != null ? _mainContainer.Margin : ZeroThickness;

        internal void SetPosition(PlacementMode preferedPlacement, UIElement placementTarget, Point mousePosition, bool secondSetPositionCall = false)
        {
            UIElement visualRoot = this.FindVisualRoot() as UIElement;
            if (visualRoot == null)
                return;

            Rect screenBounds = new Rect(visualRoot.RenderSize);

            _relativeToTargetPoint = placementTarget == null
                                         ? mousePosition
                                         : placementTarget.TransformToVisual(visualRoot).Transform(new Point(0.0, 0.0));

            _relativeToTargetPoint.Offset(_popup.HorizontalOffset, _popup.VerticalOffset);

            double arrowWidth = _arrow != null ? _arrow.ActualWidth : 0d;
            Thickness arrowMargin = _arrow != null ? _arrow.Margin : ZeroThickness;

            double horizontalOffset = placementTarget != null
                                       ? placementTarget.RenderSize.Width / 2.0 - arrowWidth / 2.0 + MainContainerMargin.Left * 0.5
                                       : arrowWidth / 2.0;

            switch (preferedPlacement)
            {
                case PlacementMode.Bottom:
                    {
                        if (ActualHeight * _dpiYfactor + _relativeToTargetPoint.Y > screenBounds.Top + screenBounds.Height)
                        {
                            PositionTop(_relativeToTargetPoint, secondSetPositionCall);
                            if (_arrow != null)
                                _arrow.Margin = new Thickness(arrowMargin.Left, -1.0, arrowMargin.Right, arrowMargin.Bottom);
                        }
                        else
                        {
                            _relativeToTargetPoint.Y += placementTarget != null ? placementTarget.RenderSize.Height * _dpiYfactor : 0.0;
                            PositionBottom(_relativeToTargetPoint, secondSetPositionCall);
                            if (_arrow != null)
                                _arrow.Margin = new Thickness(arrowMargin.Left, arrowMargin.Top, arrowMargin.Right, -1.0);
                        }

                        if (secondSetPositionCall)
                            break;

                        SetPosition(PlacementMode.Right, placementTarget, mousePosition, true);
                        break;
                    }

                case PlacementMode.Right:
                    {
                        if (ActualWidth * _dpiXfactor + _relativeToTargetPoint.X > screenBounds.Left + screenBounds.Width)
                        {
                            _relativeToTargetPoint.X += placementTarget != null ? placementTarget.RenderSize.Width * _dpiXfactor : 0.0;
                            PositionLeft(_relativeToTargetPoint, secondSetPositionCall);
                            if (_arrow != null)
                            {
                                _arrow.HorizontalAlignment = HorizontalAlignment.Right;
                                _arrow.Margin = new Thickness(arrowMargin.Left, arrowMargin.Top, horizontalOffset, arrowMargin.Bottom);
                            }
                        }
                        else
                        {
                            PositionRight(_relativeToTargetPoint, secondSetPositionCall);
                            if (_arrow != null)
                            {
                                _arrow.HorizontalAlignment = HorizontalAlignment.Left;
                                _arrow.Margin = new Thickness(horizontalOffset, arrowMargin.Top, arrowMargin.Right, arrowMargin.Bottom);
                            }
                        }

                        if (secondSetPositionCall)
                            break;

                        SetPosition(PlacementMode.Bottom, placementTarget, mousePosition, true);
                        break;
                    }

                case PlacementMode.Left:
                    {
                        if (screenBounds.Left > _relativeToTargetPoint.X - ActualWidth * _dpiXfactor)
                        {
                            PositionRight(_relativeToTargetPoint, secondSetPositionCall);
                            if (_arrow != null)
                            {
                                _arrow.HorizontalAlignment = HorizontalAlignment.Left;
                                _arrow.Margin = new Thickness(horizontalOffset, arrowMargin.Top, arrowMargin.Right, arrowMargin.Bottom);
                            }
                        }
                        else
                        {
                            _relativeToTargetPoint.X += placementTarget != null ? placementTarget.RenderSize.Width * _dpiXfactor : 0.0;
                            PositionLeft(_relativeToTargetPoint, secondSetPositionCall);
                            if (_arrow != null)
                            {
                                _arrow.HorizontalAlignment = HorizontalAlignment.Right;
                                _arrow.Margin = new Thickness(arrowMargin.Left, arrowMargin.Top, horizontalOffset, arrowMargin.Bottom);
                            }
                        }

                        if (secondSetPositionCall)
                            break;

                        SetPosition(PlacementMode.Bottom, placementTarget, mousePosition, true);
                        break;
                    }

                case PlacementMode.Top:
                    {
                        if (ActualHeight * _dpiYfactor - _relativeToTargetPoint.Y > screenBounds.Top)
                        {
                            _relativeToTargetPoint.Y += placementTarget != null ? placementTarget.RenderSize.Height * _dpiYfactor : 0.0;
                            PositionBottom(_relativeToTargetPoint, secondSetPositionCall);
                            if (_arrow != null)
                                _arrow.Margin = new Thickness(arrowMargin.Left, arrowMargin.Top, arrowMargin.Right, -1.0);
                        }
                        else
                        {
                            PositionTop(_relativeToTargetPoint, secondSetPositionCall);
                            if (_arrow != null)
                                _arrow.Margin = new Thickness(arrowMargin.Left, -1.0, arrowMargin.Right, arrowMargin.Bottom);
                        }

                        if (secondSetPositionCall)
                            break;

                        SetPosition(PlacementMode.Right, placementTarget, mousePosition, true);
                        break;
                    }
            }
        }

        private void PositionTop(Point relativePoint, bool ignoreArrowPlacement)
        {
            double y = relativePoint.Y - ActualHeight * _dpiYfactor;
            if (!ignoreArrowPlacement && _arrow != null)
            {
                DockPanel.SetDock(_arrow, Dock.Bottom);
                _arrow.LayoutTransform = new RotateTransform(180.0, 7.0, 7.0);
            }
            Canvas.SetTop(this, (y + MainContainerMargin.Bottom * _dpiYfactor) / _dpiYfactor);
        }

        private void PositionBottom(Point relativePoint, bool ignoreArrowPlacement)
        {
            double y = relativePoint.Y;
            if (!ignoreArrowPlacement && _arrow != null)
            {
                DockPanel.SetDock(_arrow, Dock.Top);
                _arrow.LayoutTransform = null;
            }
            Canvas.SetTop(this, (y - MainContainerMargin.Top * _dpiYfactor) / _dpiYfactor);
        }

        private void PositionLeft(Point relativePoint, bool ignoreArrowPlacement)
        {
            double x = relativePoint.X - ActualWidth * _dpiXfactor;
            if (!ignoreArrowPlacement && _arrow != null)
            {
                DockPanel.SetDock(_arrow, Dock.Right);
                _arrow.LayoutTransform = new RotateTransform(90.0, 7.0, 7.0);
            }
            Canvas.SetLeft(this, (x + MainContainerMargin.Right * 1.5 * _dpiXfactor) / _dpiXfactor);
        }

        private void PositionRight(Point relativePoint, bool ignoreArrowPlacement)
        {
            double x = relativePoint.X;
            if (!ignoreArrowPlacement && _arrow != null)
            {
                DockPanel.SetDock(_arrow, Dock.Left);
                _arrow.LayoutTransform = new RotateTransform(270.0, 7.0, 7.0);
            }
            Canvas.SetLeft(this, (x - MainContainerMargin.Left * 1.5 * _dpiXfactor) / _dpiXfactor);
        }
    }
}