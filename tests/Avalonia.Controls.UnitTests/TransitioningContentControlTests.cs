using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.UnitTests;
using Avalonia.VisualTree;
using Xunit;

#nullable enable

namespace Avalonia.Controls.UnitTests
{
    public class TransitioningContentControlTests
    {
        [Fact]
        public void Transition_Should_Not_Be_Run_When_First_Shown()
        {
            using var app = Start();
            var (target, transition) = CreateTarget("foo");

            Assert.Equal(0, transition.StartCount);
        }

        [Fact]
        public void TransitionContentPresenter_Should_Initially_Be_Hidden()
        {
            using var app = Start();
            var (target, transition) = CreateTarget("foo");
            var transitionPresenter = GetTransitionContentPresenter(target);

            Assert.False(transitionPresenter.IsVisible);
        }

        [Fact]
        public void Transition_Should_Be_Run_On_Layout()
        {
            using var app = Start();
            var (target, transition) = CreateTarget("foo");

            target.Content = "bar";
            Assert.Equal(0, transition.StartCount);

            Layout(target);
            Assert.Equal(1, transition.StartCount);
        }

        [Fact]
        public void Control_Transition_Should_Be_Run_On_Layout()
        {
            using var app = Start();
            var (target, transition) = CreateTarget(new Button());

            target.Content = new Canvas();
            Assert.Equal(0, transition.StartCount);

            Layout(target);
            Assert.Equal(1, transition.StartCount);
        }

        [Fact]
        public void ContentPresenters_Should_Be_Setup_For_Transition()
        {
            using var app = Start();
            var (target, transition) = CreateTarget("foo");
            var transitionPresenter = GetTransitionContentPresenter(target);

            target.Content = "bar";
            Layout(target);

            Assert.True(transitionPresenter.IsVisible);
            Assert.Equal("bar", target.Presenter!.Content);
            Assert.Equal("foo", transitionPresenter.Content);
        }

        [Fact]
        public void TransitionContentPresenter_Should_Be_Hidden_When_Transition_Completes()
        {
            using var app = Start();
            using var sync = UnitTestSynchronizationContext.Begin();
            var (target, transition) = CreateTarget("foo");
            var transitionPresenter = GetTransitionContentPresenter(target);

            target.Content = "bar";
            Layout(target);
            Assert.True(transitionPresenter.IsVisible);

            transition.Complete();
            sync.ExecutePostedCallbacks();

            Assert.False(transitionPresenter.IsVisible);
        }

        [Fact]
        public void Transition_Should_Be_Canceled_If_Content_Changes_While_Running()
        {
            using var app = Start();
            using var sync = UnitTestSynchronizationContext.Begin();
            var (target, transition) = CreateTarget("foo");
            var transitionPresenter = GetTransitionContentPresenter(target);

            target.Content = "bar";
            Layout(target);
            target.Content = "baz";

            Assert.Equal(0, transition.CancelCount);

            Layout(target);

            Assert.Equal(1, transition.CancelCount);
        }

        [Fact]
        public void New_Transition_Should_Be_Started_If_Content_Changes_While_Running()
        {
            using var app = Start();
            using var sync = UnitTestSynchronizationContext.Begin();
            var (target, transition) = CreateTarget("foo");
            var transitionPresenter = GetTransitionContentPresenter(target);

            target.Content = "bar";
            Layout(target);

            target.Content = "baz";

            var startedRaised = 0;

            transition.Started += (from, to, forward) =>
            {
                var fromPresenter = Assert.IsType<ContentPresenter>(from);
                var toPresenter = Assert.IsType<ContentPresenter>(to);

                Assert.Same(transitionPresenter, fromPresenter);
                Assert.Same(target.Presenter, toPresenter);
                Assert.Equal("bar", fromPresenter.Content);
                Assert.Equal("baz", toPresenter.Content);
                Assert.True(forward);
                Assert.Equal(1, transition.CancelCount);

                ++startedRaised;
            };

            Layout(target);
            sync.ExecutePostedCallbacks();

            Assert.Equal(1, startedRaised);
            Assert.Equal("baz", target.Presenter!.Content);
            Assert.Equal("bar", transitionPresenter.Content);
        }

        private static IDisposable Start()
        {
            return UnitTestApplication.Start(
                TestServices.MockThreadingInterface.With(
                    fontManagerImpl: new HeadlessFontManagerStub(),
                    renderInterface: new HeadlessPlatformRenderInterface(),
                    textShaperImpl: new HeadlessTextShaperStub()));
        }

        private static (TransitioningContentControl, TestTransition) CreateTarget(object content)
        {
            var transition = new TestTransition();
            var target = new TransitioningContentControl
            {
                Content = content,
                PageTransition = transition,
                Template = CreateTemplate(),
            }; 

            var root = new TestRoot(target);
            root.LayoutManager.ExecuteInitialLayoutPass();
            return (target, transition);
        }

        private static IControlTemplate CreateTemplate()
        {
            return new FuncControlTemplate((x, ns) =>
            {
                return new Panel
                {
                    Children =
                    {
                        new ContentPresenter
                        {
                            Name = "PART_ContentPresenter",
                            [!ContentPresenter.ContentProperty] = x[!ContentControl.ContentProperty],
                        },
                        new ContentPresenter
                        {
                            Name = "PART_TransitionContentPresenter",
                        },
                    }
                };
            });
        }

        private static ContentPresenter GetTransitionContentPresenter(TransitioningContentControl target)
        {
            return Assert.IsType<ContentPresenter>(target
                .GetTemplateChildren()
                .First(x => x.Name == "PART_TransitionContentPresenter"));
        }

        private void Layout(Control c)
        {
            (c.GetVisualRoot() as ILayoutRoot)?.LayoutManager.ExecuteLayoutPass();
        }

        private class TestTransition : IPageTransition
        {
            private TaskCompletionSource? _tcs;

            public int StartCount { get; private set; }
            public int FinishCount { get; private set; }
            public int CancelCount { get; private set; }

            public event Action<Visual?, Visual?, bool>? Started;

            public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
            {
                ++StartCount;
                Started?.Invoke(from, to, forward);
                if (_tcs is not null)
                    throw new InvalidOperationException("Transition already running");
                _tcs = new TaskCompletionSource();
                cancellationToken.Register(() => _tcs.TrySetResult());
                await _tcs.Task;
                _tcs = null;

                if (!cancellationToken.IsCancellationRequested)
                    ++FinishCount;
                else
                    ++CancelCount;
            }

            public void Complete() => _tcs!.TrySetResult();
        }
    }
}