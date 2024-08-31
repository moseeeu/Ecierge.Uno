namespace Ecierge.Uno.Navigation;

using System;

using Ecierge.Uno.Navigation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.UI.Dispatching;

public sealed class NavigationScope : IServiceScope, IDisposable
{
    private static readonly Type WindowType = typeof(Window);
    private static readonly Type FrameworkElementType = typeof(FrameworkElement);
    private static readonly Type ContentDialogType = typeof(ContentDialog);
    private static readonly Type DispatcherType = typeof(DispatcherQueue);
    private static readonly Type NavigationScopeType = typeof(NavigationScope);
    private static readonly Type NameSegmentType = typeof(NameSegment);
    private static readonly Type NavigatorType = typeof(Navigator);

    private readonly IServiceScope serviceScope;

    internal NameSegment Segment { get; }

    public IServiceProvider ServiceProvider => serviceScope.ServiceProvider;

    private NavigationScope(IServiceScope serviceScope, Window window, NameSegment segment, Navigator? parentNavigator)
    {
        this.serviceScope = serviceScope ?? throw new ArgumentNullException(nameof(serviceScope));
        window = window ?? throw new ArgumentNullException(nameof(window));
        this.Segment = segment ?? throw new ArgumentNullException(nameof(segment));

        var serviceProvider = this.ServiceProvider;
        serviceProvider.AddScopedInstance(WindowType, window);
        serviceProvider.AddScopedInstance(DispatcherType, window.DispatcherQueue);
        serviceProvider.AddScopedInstance(NavigationScopeType, this);
        serviceProvider.AddScopedInstance(NameSegmentType, segment);
    }

    public NavigationScope(IServiceScope serviceScope, Window window, NameSegment segment, FrameworkElement element, Navigator? parentNavigator)
        : this(serviceScope, window, segment, parentNavigator)
    {
        element = element ?? throw new ArgumentNullException(nameof(element));

        var serviceProvider = this.ServiceProvider;
        serviceProvider.AddScopedInstance(FrameworkElementType, element);
        serviceProvider.AddScopedInstance(NavigatorType, GetNavigator(element, parentNavigator));
    }

    private bool isDisposed;

    ~NavigationScope() => Dispose(false);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            serviceScope.Dispose();
        }
    }

    public NavigationScope CreateScope(NameSegment segment, FrameworkElement element, Navigator parentNavigator)
     => new NavigationScope(
         this.ServiceProvider.CreateScope(),
         this.ServiceProvider.GetRequiredService<Window>(),
         segment,
         element,
         parentNavigator);

    public NavigationScope CreateDialogScope(DialogSegment segment, Navigator parentNavigator)
    {
        var lastNavigator = parentNavigator;
        while (lastNavigator.ChildNavigator is not null)
        {
            lastNavigator = lastNavigator.ChildNavigator;
        }

        var serviceProvider = lastNavigator.Region.Scope.ServiceProvider;
        var navigationScope = new NavigationScope(
            serviceProvider.CreateScope(),
            serviceProvider.GetRequiredService<Window>(),
            segment,
            lastNavigator);
        serviceProvider = navigationScope.ServiceProvider;

        var options = serviceProvider.GetRequiredService<IOptions<NavigationOptions>>().Value;
        Type viewType = segment.ViewMap!.View;
        Type navigatorType;
        if (ContentDialogType.IsAssignableFrom(viewType))
        {
            if (!options.TryGetNavigatorType(viewType, out navigatorType!))
                throw new InvalidOperationException($"No navigator found for {viewType.Name}");
        }
        else
        {
            if (!options.TryGetNavigatorType(ContentDialogType, out navigatorType!))
                throw new InvalidOperationException($"No navigator found for {viewType.Name}");
        }
        Navigator navigator = (Navigator)serviceProvider.GetRequiredService(navigatorType);
        AssginNavigators(lastNavigator, navigator);
        serviceProvider.AddScopedInstance(NavigatorType, navigator);
        lastNavigator.ChildNavigator = navigator;
        return navigationScope;
    }

    private Navigator GetNavigator(FrameworkElement element, Navigator? parent)
    {
        Type? navigatorType = element.GetNavigatorType();
        if (navigatorType is null)
        {
            Type elementType = element.GetType();
            var options = this.ServiceProvider.GetRequiredService<IOptions<NavigationOptions>>().Value;
            if (!options.TryGetNavigatorType(elementType, out navigatorType))
                throw new InvalidOperationException($"No navigator found for {elementType.Name}");
        }
        var navigator = (Navigator)this.ServiceProvider.GetRequiredService(navigatorType);
        AssginNavigators(parent, navigator);
        return navigator;
    }

    private void AssginNavigators(Navigator? parent, Navigator navigator)
    {
        navigator.Parent = parent;
        if (parent is not null)
        {
            parent.ChildNavigator = navigator;
            navigator.RootNavigator = parent.RootNavigator;
        }
    }

    public NavigationResult CreateViewModel(NavigationRequest request, INavigationData? navigationData)
    {
        var data = navigationData ?? NavigationData.Empty;

        var nameSegment = request.NameSegment;
        var viewModelType = request.View!.ViewModel!;

        var viewModel = data.GetData(viewModelType);
        if (viewModel is not null) return new NavigationResult(nameSegment, viewModel);

        if (request is not DataSegmentNavigationRequest)
        {
            try
            {
                viewModel = ServiceProvider.GetService(viewModelType);
            }
            catch (InvalidOperationException) { }
            if (viewModel is not null) return new NavigationResult(nameSegment, viewModel);
        }
        if (request is DataSegmentNavigationRequest dataRequest && dataRequest.RouteData is not null)
        {
            navigationData = (navigationData ?? NavigationData.Empty).Add(dataRequest.Segment.Name, dataRequest.RouteData);
        }

        var ctor = viewModelType.GetNavigationConstructor(ServiceProvider, navigationData ?? NavigationData.Empty, out var args);
        if (ctor is not null)
        {
            try
            {
                return new NavigationResult(nameSegment, ctor.Invoke(args));
            }
            catch
            {
                ServiceProvider.GetRequiredService<ILogger<NavigationScope>>().LogInformation("Failed to create view model of type {ViewModelType} using route data and service provider", viewModelType);
                return new NavigationResult($"Failed to create view model of type {viewModelType} using route data and service provider");
            }
        }
        return new NavigationResult($"Constructor for {viewModelType} not found");
    }
}
