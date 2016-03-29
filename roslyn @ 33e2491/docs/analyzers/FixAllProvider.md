﻿Introduction
============

This document covers the following:
 - Introductory definitions
 - What is a FixAll occurrences code fix?
 - How is a FixAll occurrences code fix computed?
 - Adding FixAll support to your code fixer
 - Selecting an Equivalence key for code actions
 - Spectrum of FixAll providers
 - Built-in FixAllProvider and its limitations
 - Implementing a custom FixAllProvider

Definitions
===========

 - **Analyzer:** An instance of a type derived from `DiagnosticAnalyzer` that reports diagnostics.
 - **Code fixer:** An instance of a type derived from `CodeFixProvider` that provides code fixes for compiler and/or analyzer diagnostics.
 - **Code refactoring:** An instance of a type derived from `CodeRefactoringProvider` that provides source code refactorings.
 - **Code action:** An action registered by `CodeFixProvider.RegisterCodeFixesAsync` that performs a code fix OR an action registered by `CodeRefactoringProvider.ComputeRefactoringsAsync` that performs a code refactoring.
 - **Equivalence Key:** A string value representing an equivalence class of all code actions registered by a code fixer or refactoring. Two code actions are treated as equivalent if they have equal non-null `EquivalenceKey` values and were generated by the same code fixer or refactoring.
 - **FixAll provider:** An instance of a type derived from `FixAllProvider` that provides a FixAll occurrences code fix. A FixAll provider is associated with a corresponding code fixer by `CodeFixProvider.GetFixAllProvider` method.
 - **FixAll occurrences code fix:** A code action returned by `FixAllProvider.GetFixAsync`, that fixes all or multiple occurrences of diagnostics fixed by the corresponding code fixer, within a given `FixAllScope`.

What is a FixAll occurrences code fix?
======================================

In layman terms, a FixAll occurrences code fix means: I have a code fix 'C', that fixes a specific instance of diagnostic 'D' in my source and I want to apply this fix to all instances of 'D' across a broader scope, such as a document or a project or the entire solution.

In more technical terms: Given a particular code action registered by a code fixer to fix one or more diagnostics, a corresponding code action registered by its FixAll provider, that applies the original trigger code action across a broader scope (such as a document/project/solution) to fix multiple instances of such diagnostics.

How is a FixAll occurrences code fix computed?
==============================================

Following steps are used to compute a FixAll occurrences code fix:
 - Given a specific instance of a diagnostic, compute the set of code actions that claim to fix the diagnostic.
 - Select a specific code action from this set. In the Visual Studio IDE, this is done by selecting a specific code action in the light bulb menu.
 - The equivalence key of the selected code action represents the class of code actions that must be applied as part of a FixAll occurrences code fix.
 - Given this code action, get the FixAll provider corresponding to the code fixer that registered this code action.
 - If non-null, then request the FixAll provider for its supported FixAllScopes.
 - Select a specific `FixAllScope` from this set. In the Visual Studio IDE, this is done by clicking on the scope hyperlink in the preview dialog.
 - Given the trigger diagnostic(s), the equivalence key of the trigger code action, and the FixAll scope, invoke `FixAllProvider.GetFixAsync` to compute the FixAll occurrences code fix.

Adding FixAll support to your code fixer
========================================

Follow the below steps to add FixAll support to your code fixer:
 - Override the `CodeFixProvider.GetFixAllProvider` method and return a non-null instance of a `FixAllProvider`. You may either use our built-in FixAllProvider or implement a custom FixAllProvider. See the following sections in this document for determining the correct approach for your fixer.
 - Ensure that all the code actions registered by your code fixer have a non-null equivalence key. See the following section to determine how to select an equivalence key.

Selecting an Equivalence key for code actions
=============================================

Each unique equivalence key for a code fixer defines a unique equivalence class of code actions. Equivalence key of the trigger code action is part of the `FixAllContext` and is used to determine the FixAll occurrences code fix.
Normally, you can use the **'title'** of the code action as the equivalence key. However, there are cases where you may desire to have different values. Let us take an example to get a better understanding.

Let us consider the [C# SimplifyTypeNamesCodeFixProvider](http://source.roslyn.io/#q=Microsoft.CodeAnalysis.CSharp.CodeFixes.SimplifyTypeNames.SimplifyTypeNamesCodeFixProvider) that registers multiple code actions and also has FixAll support. This code fixer offers fixes to simplify the following expressions:
 - `this` expressions of the form 'this.x' to 'x'.
 - Qualified type names of the form 'A.B' to 'B'.
 - Member access expressions of the form 'A.M' to 'M'.

This fixer needs the following semantics for the corresponding FixAll occurrences code fixes:
 - `this` expression simplification: Fix all should simplify all this expressions, regardless of the member being accessed (this.x, this.y, this.z, etc.).
 - Qualified type name simplification: Fix all should simplify all qualified type names 'A.B' to 'B'. However, we don't want to simplify **all** qualified type names, such as 'C.D', 'E.F', etc. as that would be too generic a fix, which is not likely intended by the user.
 - Member access expressions: Fix all should simplify all member access expressions 'A.M' to 'M'.

It uses the below equivalence keys for it's registered code actions to get the desired FixAll behavior:
 - `this` expression simplification: Generic resource string "Simplify this expression", which explicitly excludes the contents of the node being simplified.
 - Qualified type name simplification: Formatted resource string "Simplify type name A.B", which explicitly includes the contents of the node being simplified.
 - Member access expressions: Formatted resource string "Simplify type name A.M", which explicitly includes the contents of the node being simplified.

Note that '`this` expression simplification' fix requires a different kind of an equivalence class from the other two simplifications. See method [GetCodeActionId](http://source.roslyn.io/Microsoft.CodeAnalysis.CSharp.Features/R/917a728e9783562f.html) for the actual implementation.

To summarize, use the equivalence key that best suits the category of fixes to be applied as part of a FixAll operation.

Spectrum of FixAll providers
============================

When multiple fixes need to be applied to documents, there are various way to do it:
- **Sequential approach**: One way to do it is to compute diagnostics, pick one, ask a fixer to produce a code action to fix that, apply it. Now for the resulting new compilation, recomputed diagnostics, pick the next one and repeat the process. This approach would be very slow but would lead to correct results (unless it doesn't converge where one fix introduces a diagnostic that was just fixed by a previous fix). We chose to not implement this approach.
- **Batch fix approach** - Another way to do this to compute all the diagnostics, pick each diagnostic and give it to a fixer to and ask it apply it to produce a new solution. If there were 'n' diagnostics, there would be 'n' new solutions. Now just merge them all together in one go. This may produce incorrect results (when different fixes change the same region of code in different ways) but it is very fast. We have one implementation of this approach in `WellKnownFixAllProviders.BatchFixer`
- **Custom approach** - Depending on the fix, there may be a custom solution to fix multiple issues. For example, consider an analyzer that simply needs to generate one file as the fix for any instance of the issue. Instead of generating the same file over and over using the previous two approaches, one could write a custom `FixAllProvider` that simply generates the file once if there were any diagnostics at all.

Since there are various ways of fixing all issues, we've implemented a framework and provided the one general implementation that we think is useful in many cases.

Built-in FixAllProvider
=======================

We provide a default `BatchFixAllProvider` implementation of a FixAll provider that uses the underlying code fixer to compute the FixAll occurrences code fixes.
To use the batch fixer, you should return the static `WellKnownFixAllProviders.BatchFixer` instance in the `CodeFixProvider.GetFixAllProvider` override.
NOTE: See the following section on **'Limitations of the BatchFixer'** to determine if the batch fixer can be used by your code fixer.

Given a trigger diagnostic, a trigger code action, the underlying code fixer and the FixAll scope, the BatchFixer computes FixAll occurrences code fix with the following steps:
 - Compute all instances of the trigger diagnostic across the FixAll scope.
 - For each computed diagnostic, invoke the underlying code fixer to compute the set of code actions to fix the diagnostic.
 - Collect all the registered code actions that have the same equivalence key as the trigger code action.
 - Apply all these code actions on the original solution snapshot to compute new solution snapshots. The batch fixer only batches code action operations of type `ApplyChangesOperation` present within the individual code actions, other types of operations are ignored.
 - Sequentially merge the new solution snapshots into a final solution snapshot. Only non-conflicting code actions whose fix spans don't overlap the fix spans of prior merged code actions are retained.

Limitations of the BatchFixer
=============================

The BatchFixer is designed for a common category of fixers where fix spans for diagnostics don't overlap with each other. For example, assume there is a diagnostic that spans a particular expression, and a fixer that fixes that expression. If all the instances of this diagnostic are guaranteed to have non-overlapping spans, then their fixes can be computed independently and this batch of fixes can be subsequently merged together.

However, there are cases where the BatchFixer might not work for your fixer. Following are some such examples:
 - Code fixer registers code actions without an equivalence key or with a null equivalence key.
 - Code fixer registers non-local code actions, i.e. a code action whose fix span is completely distinct from diagnostic span. For example, a fix that adds a new declaration node. Multiple such fixes are likely to have overlapping spans and hence could be conflicting.
 - Diagnostics to be fixed as part of FixAll occurrences have overlapping spans. It is likely that fixes for such diagnostics will have overlapping spans too, and hence would conflict with each other.
 - Code fixer registers code actions with operations other than `ApplyChangesOperation`. BatchFixer ignores such operations and hence may produce unexpected results.

Implementing a custom FixAllProvider
====================================

For cases where you cannot use the BatchFixer, you must implement your own `FixAllProvider`. It is recommended that you create a singleton instance of the FixAll provider, instead of creating a new instance for every `CodeFixProvider.GetFixAllProvider` invocation.
Following guidelines should help in the implementation:
 - **GetFixAsync:** Primary method to compute the FixAll occurrences code fix for a given `FixAllContext`. You may use the set of 'GetXXXDiagnosticsAsync' methods on the `FixAllContext` to compute the diagnostics to be fixed. You must return a single code action that fixes all the diagnostics in the given FixAll scope.
 - **GetSupportedFixAllScopes:** Virtual method to get all the supported FixAll scopes. By default, it returns all the three supported scopes: document, project and solution scopes. Generally, you need not override this method. However, you may do so if you wish to support a subset of these scopes.
 - **GetSupportedFixAllDiagnosticIds:** Virtual method to get all the fixable diagnostic ids. By default, it returns the underlying code fixer's `FixableDiagnosticIds`. Generally, you need not override this method. However, you may do so if you wish to support FixAll only for a subset of these ids.

See [DeclarePublicAPIFix](https://github.com/dotnet/roslyn-analyzers/blob/master/src/Unfactored/Roslyn/Core/ApiDesign/DeclarePublicAPIFix.cs) for an example implementation of a custom FixAllProvider.