// Copyright (c) The Avalonia Project. All rights reserved.
// Licensed under the MIT license. See licence.md file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Collections;
using Avalonia.Controls.Utils;
using Avalonia.VisualTree;
using JetBrains.Annotations;

namespace Avalonia.Controls
{
    /// <summary>
    /// Lays out child controls according to a grid.
    /// </summary>
    public class Grid : Panel
    {
        /// <summary>
        /// Defines the Column attached property.
        /// </summary>
        public static readonly AttachedProperty<int> ColumnProperty =
            AvaloniaProperty.RegisterAttached<Grid, Control, int>(
                "Column",
                validate: ValidateColumn);

        /// <summary>
        /// Defines the ColumnSpan attached property.
        /// </summary>
        public static readonly AttachedProperty<int> ColumnSpanProperty =
            AvaloniaProperty.RegisterAttached<Grid, Control, int>("ColumnSpan", 1);

        /// <summary>
        /// Defines the Row attached property.
        /// </summary>
        public static readonly AttachedProperty<int> RowProperty =
            AvaloniaProperty.RegisterAttached<Grid, Control, int>(
                "Row",
                validate: ValidateRow);

        /// <summary>
        /// Defines the RowSpan attached property.
        /// </summary>
        public static readonly AttachedProperty<int> RowSpanProperty =
            AvaloniaProperty.RegisterAttached<Grid, Control, int>("RowSpan", 1);

        public static readonly AttachedProperty<bool> IsSharedSizeScopeProperty =
            AvaloniaProperty.RegisterAttached<Grid, Control, bool>("IsSharedSizeScope", false);

        private sealed class SharedSizeScopeHost : IDisposable
        {
            private class GridMeasureCache
            {
                public Grid Grid { get; }
                public DefinitionBase Definition { get; }
                public double CachedLength { get; set; }
            }

            private readonly AvaloniaList<Grid> _participatingGrids;

            private Dictionary<string, double> _cachedSize = new Dictionary<string, double>();

            private Dictionary<string, List<Grid>> _gridsInScopes = new Dictionary<string, List<Grid>>(); 

            private Dictionary<string, List<GridMeasureCache>> _scopeCache;
            private int _leftToMeasure;

            public SharedSizeScopeHost(Control scope)
            {
                _participatingGrids = GetParticipatingGrids(scope);
                
                foreach (var grid in _participatingGrids)
                {
                    grid.InvalidateMeasure();
                    AddGridToScopes(grid);
                }
            }

            private bool _invalidating = false;

            internal void InvalidateMeasure(Grid grid)
            {
                if (_invalidating)
                    return;
                _invalidating = true;

                List<Grid> candidates = new List<Grid> {grid};
                while (candidates.Any())
                {
                    var scopes = candidates.SelectMany(c => c.RowDefinitions.Select(rd => rd.SharedSizeGroup))
                         .Concat(candidates.SelectMany(c => c.ColumnDefinitions.Select(rd => rd.SharedSizeGroup))).Distinct();
                    
                    candidates = scopes.SelectMany(r => _scopeCache[r].Select(gmc => gmc.Grid))
                                 .Distinct().Where(c => c.IsMeasureValid).ToList();
                    candidates.ForEach(c => c.InvalidateMeasure());
                }

                _invalidating = false;
            }

            private void AddGridToScopes(Grid grid)
            {
                var scopeNames = grid.ColumnDefinitions.Select(g => g.SharedSizeGroup)
                                 .Concat(grid.RowDefinitions.Select(g => g.SharedSizeGroup)).Distinct();
                foreach (var scopeName in scopeNames)
                {
                    if (!_gridsInScopes.TryGetValue(scopeName, out var list))
                        _gridsInScopes.Add(scopeName, list = new List<Grid>() );
                    list.Add(grid);
                }
            }

            private void RemoveGridFromScopes(Grid grid)
            {
                var scopeNames = grid.ColumnDefinitions.Select(g => g.SharedSizeGroup)
                    .Concat(grid.RowDefinitions.Select(g => g.SharedSizeGroup)).Distinct();
                foreach (var scopeName in scopeNames)
                {
                    Debug.Assert(_gridsInScopes.TryGetValue(scopeName, out var list));
                    list.Remove(grid);
                    if (!list.Any())
                        _gridsInScopes.Remove(scopeName);
                }
            }

            internal void UpdateMeasureResult(GridLayout.MeasureResult result, ColumnDefinitions columnDefinitions)
            {
                for (var i = 0; i < columnDefinitions.Count; i++)
                {
                    if (string.IsNullOrEmpty(columnDefinitions[i].SharedSizeGroup))
                        continue;
                    // if any in this group is Absolute we don't care about measured values.
                    
                }
            }

            internal void UpdateMeasureResult(GridLayout.MeasureResult result, RowDefinitions rowDefinitions)
            {

            }

            internal double GetExistingLimit(DefinitionBase definition)
            {
                List<GridMeasureCache> cache = _scopeCache[definition.SharedSizeGroup];

                return cache.Where(gmc => gmc.Grid.IsMeasureValid)
                    .Aggregate(double.NaN, (a, gmc) => Math.Max(a, gmc.CachedLength));
            }

            internal void UpdateExistingLimit(DefinitionBase definition, double limit)
            {
                List<GridMeasureCache> cache = _scopeCache[definition.SharedSizeGroup];

                cache.Single(gmc => ReferenceEquals(gmc.Definition, definition)).CachedLength = limit;
                // if any other are lower - invalidate the grid.
            }

            internal void BeginMeasurePass()
            {
                if (_leftToMeasure == 0)
                {
                    _leftToMeasure = _participatingGrids.Count(g => !g.IsMeasureValid);
                }
            }

            private static AvaloniaList<Grid> GetParticipatingGrids(Control scope)
            {
                var result = scope.GetVisualDescendants().OfType<Grid>();

                return new AvaloniaList<Grid>(result.Where(g => g.HasSharedSizeGroups()));
            }

            public void Dispose()
            {
                foreach (var grid in _participatingGrids)
                {
                    grid.SharedScopeChanged();
                }
            }

            internal void RegisterGrid(Grid toAdd)
            {
                Debug.Assert(!_participatingGrids.Contains(toAdd));
                _participatingGrids.Add(toAdd);
                AddGridToScopes(toAdd);
            }

            internal void UnegisterGrid(Grid toRemove)
            {
                Debug.Assert(_participatingGrids.Contains(toRemove));
                _participatingGrids.Remove(toRemove);
                RemoveGridFromScopes(toRemove);
            }
        }

        protected override void OnMeasureInvalidated()
        {
            base.OnMeasureInvalidated();
            _sharedSizeHost?.InvalidateMeasure(this);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            var scope = this.GetVisualAncestors().OfType<Control>()
                .FirstOrDefault(c => c.GetValue(IsSharedSizeScopeProperty));

            Debug.Assert(_sharedSizeHost == null);

            if (scope != null)
            {
                _sharedSizeHost = scope.GetValue(s_sharedSizeScopeHostProperty);
                _sharedSizeHost.RegisterGrid(this);
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            _sharedSizeHost?.UnegisterGrid(this);
            _sharedSizeHost = null;
        }

        private SharedSizeScopeHost _sharedSizeHost;

        private static readonly AttachedProperty<SharedSizeScopeHost> s_sharedSizeScopeHostProperty =
            AvaloniaProperty.RegisterAttached<Grid, Control, SharedSizeScopeHost>("&&SharedSizeScopeHost", null);

        private ColumnDefinitions _columnDefinitions;

        private RowDefinitions _rowDefinitions;

        static Grid()
        {
            AffectsParentMeasure<Grid>(ColumnProperty, ColumnSpanProperty, RowProperty, RowSpanProperty);
            IsSharedSizeScopeProperty.Changed.AddClassHandler<Control>(IsSharedSizeScopeChanged);
        }

        private static void IsSharedSizeScopeChanged(Control source, AvaloniaPropertyChangedEventArgs arg2) 
        {
            if ((bool)arg2.NewValue)
            {
                Debug.Assert(source.GetValue(s_sharedSizeScopeHostProperty) == null);
                source.SetValue(IsSharedSizeScopeProperty, new SharedSizeScopeHost(source));
            }
            else
            {
                var host = source.GetValue(s_sharedSizeScopeHostProperty) as SharedSizeScopeHost;
                Debug.Assert(host != null);
                host.Dispose();
                source.SetValue(IsSharedSizeScopeProperty, null);
            }
        }

        /// <summary>
        /// Gets or sets the columns definitions for the grid.
        /// </summary>
        public ColumnDefinitions ColumnDefinitions
        {
            get
            {
                if (_columnDefinitions == null)
                {
                    ColumnDefinitions = new ColumnDefinitions();
                }

                return _columnDefinitions;
            }

            set
            {
                if (_columnDefinitions != null)
                {
                    throw new NotSupportedException("Reassigning ColumnDefinitions not yet implemented.");
                }

                _columnDefinitions = value;
                _columnDefinitions.TrackItemPropertyChanged(_ => InvalidateMeasure());
            }
        }

        /// <summary>
        /// Gets or sets the row definitions for the grid.
        /// </summary>
        public RowDefinitions RowDefinitions
        {
            get
            {
                if (_rowDefinitions == null)
                {
                    RowDefinitions = new RowDefinitions();
                }

                return _rowDefinitions;
            }

            set
            {
                if (_rowDefinitions != null)
                {
                    throw new NotSupportedException("Reassigning RowDefinitions not yet implemented.");
                }

                _rowDefinitions = value;
                _rowDefinitions.TrackItemPropertyChanged(_ => InvalidateMeasure());
            }
        }

        /// <summary>
        /// Gets the value of the Column attached property for a control.
        /// </summary>
        /// <param name="element">The control.</param>
        /// <returns>The control's column.</returns>
        public static int GetColumn(AvaloniaObject element)
        {
            return element.GetValue(ColumnProperty);
        }

        /// <summary>
        /// Gets the value of the ColumnSpan attached property for a control.
        /// </summary>
        /// <param name="element">The control.</param>
        /// <returns>The control's column span.</returns>
        public static int GetColumnSpan(AvaloniaObject element)
        {
            return element.GetValue(ColumnSpanProperty);
        }

        /// <summary>
        /// Gets the value of the Row attached property for a control.
        /// </summary>
        /// <param name="element">The control.</param>
        /// <returns>The control's row.</returns>
        public static int GetRow(AvaloniaObject element)
        {
            return element.GetValue(RowProperty);
        }

        /// <summary>
        /// Gets the value of the RowSpan attached property for a control.
        /// </summary>
        /// <param name="element">The control.</param>
        /// <returns>The control's row span.</returns>
        public static int GetRowSpan(AvaloniaObject element)
        {
            return element.GetValue(RowSpanProperty);
        }

        /// <summary>
        /// Sets the value of the Column attached property for a control.
        /// </summary>
        /// <param name="element">The control.</param>
        /// <param name="value">The column value.</param>
        public static void SetColumn(AvaloniaObject element, int value)
        {
            element.SetValue(ColumnProperty, value);
        }

        /// <summary>
        /// Sets the value of the ColumnSpan attached property for a control.
        /// </summary>
        /// <param name="element">The control.</param>
        /// <param name="value">The column span value.</param>
        public static void SetColumnSpan(AvaloniaObject element, int value)
        {
            element.SetValue(ColumnSpanProperty, value);
        }

        /// <summary>
        /// Sets the value of the Row attached property for a control.
        /// </summary>
        /// <param name="element">The control.</param>
        /// <param name="value">The row value.</param>
        public static void SetRow(AvaloniaObject element, int value)
        {
            element.SetValue(RowProperty, value);
        }

        /// <summary>
        /// Sets the value of the RowSpan attached property for a control.
        /// </summary>
        /// <param name="element">The control.</param>
        /// <param name="value">The row span value.</param>
        public static void SetRowSpan(AvaloniaObject element, int value)
        {
            element.SetValue(RowSpanProperty, value);
        }

        /// <summary>
        /// Gets the result of the last column measurement.
        /// Use this result to reduce the arrange calculation.
        /// </summary>
        private GridLayout.MeasureResult _columnMeasureCache;

        /// <summary>
        /// Gets the result of the last row measurement.
        /// Use this result to reduce the arrange calculation.
        /// </summary>
        private GridLayout.MeasureResult _rowMeasureCache;

        /// <summary>
        /// Gets the row layout as of the last measure.
        /// </summary>
        private GridLayout _rowLayoutCache;

        /// <summary>
        /// Gets the column layout as of the last measure.
        /// </summary>
        private GridLayout _columnLayoutCache;

        /// <summary>
        /// Measures the grid.
        /// </summary>
        /// <param name="constraint">The available size.</param>
        /// <returns>The desired size of the control.</returns>
        protected override Size MeasureOverride(Size constraint)
        {
            // Situation 1/2:
            // If the grid doesn't have any column/row definitions, it behaves like a normal panel.
            // GridLayout supports this situation but we handle this separately for performance.

            if (ColumnDefinitions.Count == 0 && RowDefinitions.Count == 0)
            {
                var maxWidth = 0.0;
                var maxHeight = 0.0;
                foreach (var child in Children.OfType<Control>())
                {
                    child.Measure(constraint);
                    maxWidth = Math.Max(maxWidth, child.DesiredSize.Width);
                    maxHeight = Math.Max(maxHeight, child.DesiredSize.Height);
                }

                maxWidth = Math.Min(maxWidth, constraint.Width);
                maxHeight = Math.Min(maxHeight, constraint.Height);
                return new Size(maxWidth, maxHeight);
            }

            // Situation 2/2:
            // If the grid defines some columns or rows.
            // Debug Tip:
            //     - GridLayout doesn't hold any state, so you can drag the debugger execution
            //       arrow back to any statements and re-run them without any side-effect.

            var measureCache = new Dictionary<Control, Size>();
            var (safeColumns, safeRows) = GetSafeColumnRows();
            var columnLayout = new GridLayout(ColumnDefinitions);
            var rowLayout = new GridLayout(RowDefinitions);
            // Note: If a child stays in a * or Auto column/row, use constraint to measure it.
            columnLayout.AppendMeasureConventions(safeColumns, child => MeasureOnce(child, constraint).Width);
            rowLayout.AppendMeasureConventions(safeRows, child => MeasureOnce(child, constraint).Height);

            // Calculate measurement.
            var columnResult = columnLayout.Measure(constraint.Width);
            var rowResult = rowLayout.Measure(constraint.Height);

            // Use the results of the measurement to measure the rest of the children.
            foreach (var child in Children.OfType<Control>())
            {
                var (column, columnSpan) = safeColumns[child];
                var (row, rowSpan) = safeRows[child];
                var width = Enumerable.Range(column, columnSpan).Select(x => columnResult.LengthList[x]).Sum();
                var height = Enumerable.Range(row, rowSpan).Select(x => rowResult.LengthList[x]).Sum();

                MeasureOnce(child, new Size(width, height));
            }

            // Cache the measure result and return the desired size.
            _columnMeasureCache = columnResult;
            _rowMeasureCache = rowResult;
            _rowLayoutCache = rowLayout;
            _columnLayoutCache = columnLayout;

            return new Size(columnResult.DesiredLength, rowResult.DesiredLength);

            // Measure each child only once.
            // If a child has been measured, it will just return the desired size.
            Size MeasureOnce(Control child, Size size)
            {
                if (measureCache.TryGetValue(child, out var desiredSize))
                {
                    return desiredSize;
                }

                child.Measure(size);
                desiredSize = child.DesiredSize;
                measureCache[child] = desiredSize;
                return desiredSize;
            }
        }

        /// <summary>
        /// Arranges the grid's children.
        /// </summary>
        /// <param name="finalSize">The size allocated to the control.</param>
        /// <returns>The space taken.</returns>
        protected override Size ArrangeOverride(Size finalSize)
        {
            // Situation 1/2:
            // If the grid doesn't have any column/row definitions, it behaves like a normal panel.
            // GridLayout supports this situation but we handle this separately for performance.

            if (ColumnDefinitions.Count == 0 && RowDefinitions.Count == 0)
            {
                foreach (var child in Children.OfType<Control>())
                {
                    child.Arrange(new Rect(finalSize));
                }

                return finalSize;
            }

            // Situation 2/2:
            // If the grid defines some columns or rows.
            // Debug Tip:
            //     - GridLayout doesn't hold any state, so you can drag the debugger execution
            //       arrow back to any statements and re-run them without any side-effect.

            var (safeColumns, safeRows) = GetSafeColumnRows();
            var columnLayout = _columnLayoutCache;
            var rowLayout = _rowLayoutCache;
            // Calculate for arrange result.
            var columnResult = columnLayout.Arrange(finalSize.Width, _columnMeasureCache);
            var rowResult = rowLayout.Arrange(finalSize.Height, _rowMeasureCache);
            // Arrange the children.
            foreach (var child in Children.OfType<Control>())
            {
                var (column, columnSpan) = safeColumns[child];
                var (row, rowSpan) = safeRows[child];
                var x = Enumerable.Range(0, column).Sum(c => columnResult.LengthList[c]);
                var y = Enumerable.Range(0, row).Sum(r => rowResult.LengthList[r]);
                var width = Enumerable.Range(column, columnSpan).Sum(c => columnResult.LengthList[c]);
                var height = Enumerable.Range(row, rowSpan).Sum(r => rowResult.LengthList[r]);
                child.Arrange(new Rect(x, y, width, height));
            }

            // Assign the actual width.
            for (var i = 0; i < ColumnDefinitions.Count; i++)
            {
                ColumnDefinitions[i].ActualWidth = columnResult.LengthList[i];
            }

            // Assign the actual height.
            for (var i = 0; i < RowDefinitions.Count; i++)
            {
                RowDefinitions[i].ActualHeight = rowResult.LengthList[i];
            }

            // Return the render size.
            return finalSize;
        }

        /// <summary>
        /// Get the safe column/columnspan and safe row/rowspan.
        /// This method ensures that none of the children has a column/row outside the bounds of the definitions.
        /// </summary>
        [Pure]
        private (Dictionary<Control, (int index, int span)> safeColumns,
            Dictionary<Control, (int index, int span)> safeRows) GetSafeColumnRows()
        {
            var columnCount = ColumnDefinitions.Count;
            var rowCount = RowDefinitions.Count;
            columnCount = columnCount == 0 ? 1 : columnCount;
            rowCount = rowCount == 0 ? 1 : rowCount;
            var safeColumns = Children.OfType<Control>().ToDictionary(child => child,
                child => GetSafeSpan(columnCount, GetColumn(child), GetColumnSpan(child)));
            var safeRows = Children.OfType<Control>().ToDictionary(child => child,
                child => GetSafeSpan(rowCount, GetRow(child), GetRowSpan(child)));
            return (safeColumns, safeRows);
        }

        /// <summary>
        /// Gets the safe row/column and rowspan/columnspan for a specified range.
        /// The user may assign row/column properties outside the bounds of the row/column count, this method coerces them inside.
        /// </summary>
        /// <param name="length">The row or column count.</param>
        /// <param name="userIndex">The row or column that the user assigned.</param>
        /// <param name="userSpan">The rowspan or columnspan that the user assigned.</param>
        /// <returns>The safe row/column and rowspan/columnspan.</returns>
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (int index, int span) GetSafeSpan(int length, int userIndex, int userSpan)
        {
            var index = userIndex;
            var span = userSpan;

            if (index < 0)
            {
                span = index + span;
                index = 0;
            }

            if (span <= 0)
            {
                span = 1;
            }

            if (userIndex >= length)
            {
                index = length - 1;
                span = 1;
            }
            else if (userIndex + userSpan > length)
            {
                span = length - userIndex;
            }

            return (index, span);
        }

        private static int ValidateColumn(AvaloniaObject o, int value)
        {
            if (value < 0)
            {
                throw new ArgumentException("Invalid Grid.Column value.");
            }

            return value;
        }

        private static int ValidateRow(AvaloniaObject o, int value)
        {
            if (value < 0)
            {
                throw new ArgumentException("Invalid Grid.Row value.");
            }

            return value;
        }

        internal bool HasSharedSizeGroups()
        {
            return ColumnDefinitions.Any(cd => !string.IsNullOrEmpty(cd.SharedSizeGroup)) ||
                   RowDefinitions.Any(rd => !string.IsNullOrEmpty(rd.SharedSizeGroup));
        }

        internal void SharedScopeChanged()
        {
            _sharedSizeHost = null;
            var scope = this.GetVisualAncestors().OfType<Control>()
                .FirstOrDefault(c => c.GetValue(IsSharedSizeScopeProperty));

            if (scope != null)
            {
                _sharedSizeHost = scope.GetValue(s_sharedSizeScopeHostProperty);
                _sharedSizeHost.RegisterGrid(this);
            }

            InvalidateMeasure();
        }
    }
}
