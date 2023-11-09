﻿using System;
using System.Collections.Generic;
using Avalonia.Data;
using Avalonia.Data.Core;
using Avalonia.Data.Core.ExpressionNodes;
using Avalonia.Markup.Parsers;
using Avalonia.Markup.Xaml.MarkupExtensions.CompiledBindings;

namespace Avalonia.Markup.Xaml.MarkupExtensions
{
    public class CompiledBindingExtension : BindingBase
    {
        public CompiledBindingExtension()
        {
            Path = new CompiledBindingPath();
        }

        public CompiledBindingExtension(CompiledBindingPath path)
        {
            Path = path;
        }

        public CompiledBindingExtension ProvideValue(IServiceProvider provider)
        {
            return new CompiledBindingExtension
            {
                Path = Path,
                Converter = Converter,
                ConverterCulture = ConverterCulture,
                ConverterParameter = ConverterParameter,
                TargetNullValue = TargetNullValue,
                FallbackValue = FallbackValue,
                Mode = Mode,
                Priority = Priority,
                StringFormat = StringFormat,
                Source = Source,
                DefaultAnchor = new WeakReference(provider.GetDefaultAnchor())
            };
        }

        public override InstancedBinding? Initiate(
            AvaloniaObject target,
            AvaloniaProperty? targetProperty,
            object? anchor = null,
            bool enableDataValidation = false)
        {
            var nodes = new List<ExpressionNode>();

            // Build the expression nodes from the binding path.
            Path.BuildExpression(nodes, out var isRooted);

            // If the binding isn't rooted (i.e. doesn't have a Source or start with $parent, $self,
            // #elementName etc.) then we need to add a data context source node.
            if (Source == AvaloniaProperty.UnsetValue && !isRooted)
                nodes.Insert(0, ExpressionNodeFactory.CreateDataContext(targetProperty));

            // If the first node is an ISourceNode then allow it to select the source; otherwise
            // use the binding source if specified, falling back to the target.
            var source = nodes.Count > 0 && nodes[0] is SourceNode sn
                ? sn.SelectSource(Source, target, anchor ?? DefaultAnchor?.Target)
                : Source != AvaloniaProperty.UnsetValue? Source : target;

            // Create the binding expression and wrap it in an InstancedBinding.
            var expression = new BindingExpression(
                source,
                nodes,
                FallbackValue,
                converter: Converter,
                converterCulture: ConverterCulture,
                converterParameter: ConverterParameter,
                enableDataValidation: enableDataValidation,
                mode: ResolveBindingMode(target, targetProperty),
                stringFormat: StringFormat,
                targetNullValue: TargetNullValue,
                targetTypeConverter: TargetTypeConverter.GetDefaultConverter());

            return new InstancedBinding(expression, Mode, Priority);
        }

        /// <summary>
        /// Hack for TreeDataTemplate to create a binding expression for an item.
        /// </summary>
        /// <param name="source">The item.</param>
        /// <remarks>
        /// Ideally we'd do this in a more generic way but didn't have time to refactor
        /// ITreeDataTemplate in time for 11.0. We should revisit this in 12.0.
        /// </remarks>
        internal BindingExpression CreateObservableForTreeDataTemplate(object source)
        {
            if (Source != AvaloniaProperty.UnsetValue)
                throw new NotSupportedException("Source bindings are not supported in this context.");

            var nodes = new List<ExpressionNode>();

            Path.BuildExpression(nodes, out var isRooted);

            if (isRooted)
                throw new NotSupportedException("Rooted binding paths are not supported in this context.");

            return new BindingExpression(
                source,
                nodes,
                FallbackValue,
                converter: Converter,
                converterParameter: ConverterParameter,
                targetNullValue: TargetNullValue);
        }

        [ConstructorArgument("path")]
        public CompiledBindingPath Path { get; set; }

        public object? Source { get; set; } = AvaloniaProperty.UnsetValue;

        public Type? DataType { get; set; }
    }
}
