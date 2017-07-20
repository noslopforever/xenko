// Copyright (c) 2014-2017 Silicon Studio Corp. All rights reserved. (https://www.siliconstudio.co.jp)
// See LICENSE.md for full license information.
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SiliconStudio.Core.Annotations;
using SiliconStudio.Presentation.Controls;

namespace SiliconStudio.Presentation.ValueConverters
{
    public class TrimString : OneWayMultiValueConverter<TrimString>
    {
        public double MaxWidth { get; set; }

        public TextTrimming TextTrimming { get; set; }

        public TrimmingSource TrimmingSource { get; set; }

        public string WordSeparators { get; set; }

        /// <inheritdoc />
        public override object Convert([NotNull] object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.Length != 2)
                throw new ArgumentException("The number of provided bindings to this converter must be 2.");

            var text = values[0]?.ToString() ?? string.Empty;
            if (values[1] is Control control)
                return Trimming.ProcessTrimming(control, text, TextTrimming, TrimmingSource, WordSeparators, MaxWidth);
            if (values[1] is TextBlock textBlock)
                return Trimming.ProcessTrimming(textBlock, text, TextTrimming, TrimmingSource, WordSeparators, MaxWidth);
            return text;
        }

        /// <inheritdoc />
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            // Note: necessary because properties can be different for each instance of this converter.
            return this;
        }
    }
}
