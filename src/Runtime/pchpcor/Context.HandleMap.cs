﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pchp.Core
{
    partial class Context
    {
        /// <summary>
        /// Map of symbols declared within the context.
        /// </summary>
        /// <typeparam name="THandle">Symbol descriptor.</typeparam>
        /// <typeparam name="TComparerFactory">
        /// Factory providing string comparison object used to map symbol names.
        /// Expecting to get OrdinalIgnoreCase or Ordinal comparer.
        /// </typeparam>
        internal class HandleMap<THandle, THandleComparerFactory, TComparerFactory>
            where TComparerFactory : Utilities.IProvider<IEqualityComparer<string>>, new()
            where THandleComparerFactory : Utilities.IProvider<IEqualityComparer<THandle>>, new()
        {
            /// <summary>
            /// MultiMap of referenced symbols.
            /// Initialized lazily, applies for the whole application and all the contexts.
            /// </summary>
            readonly static Dictionary<string, THandle[]> _referencedSymbols;

            /// <summary>
            /// Names mapped to <see cref="_runtimeSymbols"/> indexes.
            /// </summary>
            readonly static Dictionary<string, int> _nameMap;

            static Queue<Action> _referencedSymbolsLoaders;

            /// <summary>
            /// Symbols declared in runtime. Indexes correspond to the <see cref="_nameMap"/>.
            /// </summary>
            THandle[] _runtimeSymbols;

            /// <summary>
            /// Handle comparer.
            /// </summary>
            readonly IEqualityComparer<THandle> _handlecomparer;

            /// <summary>
            /// Optional. Delegate invoked when a symbol redeclaration occurs.
            /// </summary>
            readonly Action<THandle> _redeclarationCallback;

            static HandleMap()
            {
                var namecomparer = (new TComparerFactory()).Create();
                _referencedSymbols = new Dictionary<string, THandle[]>(namecomparer);
                _nameMap = new Dictionary<string, int>(namecomparer);
                _referencedSymbolsLoaders = new Queue<Action>();
            }

            /// <summary>
            /// Lazily loads referenced symbols.
            /// </summary>
            static void EnsureReferencedSymbols()
            {
                while (_referencedSymbolsLoaders.Count != 0)
                {
                    _referencedSymbolsLoaders.Dequeue()();
                }
            }

            /// <summary>
            /// Initializes instance of the handle map to be used within a context.
            /// </summary>
            /// <param name="redeclarationAction">
            /// Optional. Delegate invoked when a symbol redeclaration occurs.
            /// After that the symbol declaration is overriden.
            /// </param>
            public HandleMap(Action<THandle> redeclarationAction = null)
            {
                _runtimeSymbols = new THandle[_nameMap.Count];
                _handlecomparer = (new THandleComparerFactory()).Create();

                _redeclarationCallback = redeclarationAction;
            }

            public static void LazyAddReferencedSymbols(Action action)
            {
                _referencedSymbolsLoaders.Enqueue(action);
            }

            /// <summary>
            /// Adds referenced symbol into the map.
            /// In case of redeclaration, the handle is added to the list.
            /// </summary>
            public static void AddReferencedSymbol(string name, THandle handle)
            {
                // TODO: W lock

                THandle[] handles;
                if (_referencedSymbols.TryGetValue(name, out handles))
                {
                    if (handles.Contains(handle))
                        return;

                    // note: rare case
                    var length = handles.Length;
                    Array.Resize(ref handles, length + 1);
                    handles[length] = handle;
                }
                else
                {
                    handles = new THandle[] { handle };
                }

                //
                _referencedSymbols[name] = handles;
            }

            /// <summary>
            /// Gets handles associated with given name.
            /// </summary>
            /// <param name="name">Symbol name.</param>
            /// <returns>
            /// Gets array of handles declared for given name.
            /// - Empty array in case no handle is declared.
            /// - Array with one or more items with referenced handle overloads.
            /// - Array with one item with runtime declared handle.
            /// </returns>
            public THandle[] TryGetHandle(string name)
            {
                Debug.Assert(!string.IsNullOrEmpty(name));

                EnsureReferencedSymbols();

                // lookup app tables
                THandle[] handles;
                if (_referencedSymbols.TryGetValue(name, out handles))  // TODO: RW lock
                {
                    return handles;
                }

                // lookup context tables
                int index;
                if (_nameMap.TryGetValue(name, out index))  // TODO: RW lock
                {
                    var handle = _runtimeSymbols[index];
                    if (handle != null)
                    {
                        return new THandle[] { handle };
                    }
                }

                // nothing found
                return new THandle[0];
            }

            public bool IsDeclared(ref int index, string name, THandle handle)
            {
                EnsureIndex(ref index, name);
                return index < _runtimeSymbols.Length && _handlecomparer.Equals(_runtimeSymbols[index], handle);
            }

            /// <summary>
            /// Declare handle within the runtime symbols table.
            /// </summary>
            /// <param name="index">Index variable corresponding to the name for faster declaration.</param>
            /// <param name="name">Symbol name.</param>
            /// <param name="handle">Handle of the symbol.</param>
            /// <remarks>Checks whether the symbol is not redeclared. In such case, throws an exception.</remarks>
            public void Declare(ref int index, string name, THandle handle)
            {
                Debug.Assert(!string.IsNullOrEmpty(name));
                Debug.Assert(!_referencedSymbols.ContainsKey(name));

                EnsureIndex(ref index, name);
                EnsureSize(index);
                Declare(ref _runtimeSymbols[index], handle);
            }

            void Declare(ref THandle placeholder, THandle handle)
            {
                if (!_handlecomparer.Equals(placeholder, default(THandle)) &&   // there is already a declaration
                    !_handlecomparer.Equals(placeholder, handle) &&             // the declaration is different from what we are declaring
                    _redeclarationCallback != null)                             // there is a callback to handle that
                {
                    _redeclarationCallback(handle);
                }

                // declare the symbol
                placeholder = handle;
            }

            void EnsureSize(int index)
            {
                Debug.Assert(index >= 0);

                if (_runtimeSymbols.Length <= index)
                {
                    Array.Resize(ref _runtimeSymbols, (index + 1) * 2);
                }
            }

            /// <summary>
            /// Ensures given <paramref name="index"/> is initialized and associated with its <paramref name="name"/>.
            /// </summary>
            /// <param name="index">Index variable.</param>
            /// <param name="name">Associated symbol name.</param>
            public static void EnsureIndex(ref int index, string name)
            {
                if (index <= 0)
                    index = GetIndex(name);
            }

            /// <summary>
            /// Gets index associated with given symbol name.
            /// </summary>
            static int GetIndex(string name)
            {
                int index;
                lock (_nameMap) // TODO: RW lock
                {
                    if (!_nameMap.TryGetValue(name, out index))
                    {
                        _nameMap[name] = index = _nameMap.Count + 1;
                    }
                }

                //
                return index;
            }
        }
    }
}
