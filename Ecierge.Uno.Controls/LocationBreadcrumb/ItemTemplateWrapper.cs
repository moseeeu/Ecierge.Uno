using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
#if !HAS_UNO
using RecyclePool = Ecierge.Uno.Controls.LocationBreadcrumb.RecyclePool;
using ElementFactoryRecycleArgs = Microsoft.UI.Xaml.ElementFactoryRecycleArgs;
using IElementFactoryShim = Microsoft.UI.Xaml.IElementFactory;
//UNO.UI.FeatureConfiguration
#else
using static Uno.UI.FeatureConfiguration;
using DataTemplateSelector = Microsoft.UI.Xaml.Controls.DataTemplateSelector;
using ElementFactoryRecycleArgs = Microsoft.UI.Xaml.Controls.ElementFactoryRecycleArgs;
using UIElement = Microsoft.UI.Xaml.UIElement;
using ElementFactoryGetArgs = Microsoft.UI.Xaml.Controls.ElementFactoryGetArgs;
#endif

namespace Ecierge.Uno.Controls.LocationBreadcrumb
{
    internal partial class ItemTemplateWrapper : IElementFactoryShim
    {
        private DataTemplate m_dataTemplate;
        private DataTemplateSelector m_dataTemplateSelector;

        public ItemTemplateWrapper(DataTemplate dataTemplate)
        {
            m_dataTemplate = dataTemplate;
        }

        public ItemTemplateWrapper(DataTemplateSelector dataTemplateSelector)
        {
            m_dataTemplateSelector = dataTemplateSelector;
        }

        internal DataTemplate Template
        {
            get => m_dataTemplate;
            set => m_dataTemplate = value;
        }

        internal DataTemplateSelector TemplateSelector
        {
            get => m_dataTemplateSelector;
            set => m_dataTemplateSelector = value;
        }

        public UIElement GetElement(ElementFactoryGetArgs args)
        {
            var selectedTemplate = m_dataTemplate ?? m_dataTemplateSelector.SelectTemplate(args.Data);
            // Check if selected template we got is valid
            if (selectedTemplate == null)
            {
                // Null template, use other SelectTemplate method
                try
                {
                    selectedTemplate = m_dataTemplateSelector.SelectTemplate(args.Data, null);
                }
                catch (ArgumentException)
                {
                    // The default implementation of SelectTemplate(IInspectable item, ILayout container) throws invalid arg for null container
                    // To not force everbody to provide an implementation of that, catch that here
                    //if (e.code().value != E_INVALIDARG)
                    //{
                    //	throw e;
                    //}
                }

                if (selectedTemplate == null)
                {
                    // Still null, fail with a reasonable message now.
                    throw new ArgumentException("Null encountered as data template. That is not a valid value for a data template, and can not be used.");
                }
            }

            var recyclePool = RecyclePool.GetPoolInstance(selectedTemplate);
            Microsoft.UI.Xaml.UIElement element = null;

            if (recyclePool != null)
            {
                // try to get an element from the recycle pool.
                element = recyclePool.TryGetElement("" /* key */, args.Parent as Microsoft.UI.Xaml.FrameworkElement);
            }

            if (element == null)
            {
                // no element was found in recycle pool, create a new element
                element = selectedTemplate.LoadContent() as Microsoft.UI.Xaml.FrameworkElement;

                // Template returned null, so insert empty element to render nothing
                if (element == null)
                {
                    var rectangle = new Rectangle();
                    rectangle.Width = 0;
                    rectangle.Height = 0;
                    element = rectangle;
                }

                // Associate template with element

                element.SetValue(RecyclePool.OriginTemplateProperty, selectedTemplate);
            }

            return element;
        }

        public void RecycleElement(ElementFactoryRecycleArgs args)
        {
            var element = args.Element;
            DataTemplate selectedTemplate = m_dataTemplate ?? element.GetValue(RecyclePool.OriginTemplateProperty) as DataTemplate;
            var recyclePool = RecyclePool.GetPoolInstance(selectedTemplate);
            if (recyclePool == null)
            {
                // No Recycle pool in the template, create one.
#if HAS_UNO
                recyclePool = new RecyclePool();
                RecyclePool.SetPoolInstance(selectedTemplate, recyclePool);
#endif
            }

            recyclePool.PutElement(args.Element, "" /* key */, args.Parent);
        }
    }
}
