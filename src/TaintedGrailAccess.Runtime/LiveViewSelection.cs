using System;
using System.Collections;

namespace TaintedGrailAccess.Runtime
{
    /// <summary>Finds the requested live view; a model's MainView need not be its visible menu.</summary>
    public static class LiveViewSelection
    {
        /// <summary>Enumerates the current collection without retaining a view or predicate.</summary>
        public static object Find(IEnumerable views, Type expected, Func<object, bool> visible)
        {
            if (views == null) return null;
            foreach (var view in views)
                if (view != null && expected.IsInstanceOfType(view) && visible(view)) return view;
            return null;
        }
    }
}
