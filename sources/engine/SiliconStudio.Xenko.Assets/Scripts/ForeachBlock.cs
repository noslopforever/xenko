// Copyright (c) 2011-2017 Silicon Studio Corp. All rights reserved. (https://www.siliconstudio.co.jp)
// See LICENSE.md for full license information.
using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SiliconStudio.Core;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace SiliconStudio.Xenko.Assets.Scripts
{
    public class ForeachBlock : ExecutionBlock
    {
        public static readonly SlotDefinition CollectionSlotDefinition = SlotDefinition.NewValueInput("Collection", null, null);
        //public static readonly SlotDefinition BreakSlotDefinition = SlotDefinition.NewExecutionInput("Break");

        public static readonly SlotDefinition LoopSlotDefinition = SlotDefinition.NewExecutionOutput("Loop");
        public static readonly SlotDefinition ItemSlotDefinition = SlotDefinition.NewValueOutput("Item");
        public static readonly SlotDefinition CompletedSlotDefinition = SlotDefinition.NewExecutionOutput("Completed", SlotFlags.AutoflowExecution);

        [DataMemberIgnore]
        public Slot CollectionSlot => FindSlot(CollectionSlotDefinition);

        //[DataMemberIgnore]
        //public Slot BreakSlot => FindSlot(BreakSlotDefinition);

        [DataMemberIgnore]
        public Slot LoopSlot => FindSlot(LoopSlotDefinition);

        [DataMemberIgnore]
        public Slot ItemSlot => FindSlot(ItemSlotDefinition);

        [DataMemberIgnore]
        public Slot CompletedSlot => FindSlot(CompletedSlotDefinition);

        [DataMemberIgnore]
        public override Slot OutputExecution => CompletedSlot;

        public override string Title => "Foreach";

        public override void GenerateSlots(IList<Slot> newSlots, SlotGeneratorContext context)
        {
            newSlots.Add(InputExecutionSlotDefinition);
            newSlots.Add(CollectionSlotDefinition);
            //newSlots.Add(BreakSlotDefinition);

            newSlots.Add(LoopSlotDefinition);
            newSlots.Add(ItemSlotDefinition);
            newSlots.Add(CompletedSlotDefinition);
        }

        public override void GenerateCode(VisualScriptCompilerContext context)
        {
            // Create local variable that will store the item
            var itemVariableName = context.GenerateLocalVariableName("item");
            context.RegisterLocalVariable(ItemSlot, itemVariableName);

            // Switch to "continue" instead of "return" in case there is no next node
            var wasInsideLoop = context.IsInsideLoop;
            context.IsInsideLoop = true;

            var innerBlockStatements = context.ProcessInnerLoop(LoopSlot);

            context.IsInsideLoop = wasInsideLoop;

            context.AddStatement(ForEachStatement(IdentifierName("var"), itemVariableName, context.GenerateExpression(CollectionSlot), Block(innerBlockStatements)));
        }
    }
}
