﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Mond.Compiler.Expressions.Statements
{
    class DestructuredArrayExpression : Expression, IStatementExpression
    {
        public class Index
        {
            public string Name { get; }
            public bool IsSlice { get; }

            public Index(string name, bool isSlice)
            {
                Name = name;
                IsSlice = isSlice;
            }
        }

        public ReadOnlyCollection<Index> Indices { get; }
        public Expression Initializer { get; private set; }
        public bool IsReadOnly { get; }
        public bool HasChildren => false;

        public DestructuredArrayExpression(Token token, IList<Index> indices, Expression initializer, bool isReadOnly)
            : base(token)
        {
            Indices = new ReadOnlyCollection<Index>(indices);
            Initializer = initializer;
            IsReadOnly = isReadOnly;
        }

        public override int Compile(FunctionContext context)
        {
            context.Position(Token);

            var i = 0;
            var startIndex = 0;
            var stack = Initializer?.Compile(context) ?? 1;
            var global = context.ArgIndex == 0 && context.Compiler.Options.MakeRootDeclarationsGlobal;

            foreach (var index in Indices)
            {
                var assign = context.MakeLabel("arrayDestructureAssign");
                var destruct = context.MakeLabel("arrayDestructureIndex");
                var remaining = Indices.Skip(i + 1).Count();

                stack += context.Dup();
                stack += context.Dup();
                stack += context.LoadField(context.String("length"));
                stack += context.Call(0, new List<ImmediateOperand>());
                stack += context.Load(context.Number(1));
                stack += context.BinaryOperation(TokenType.Subtract);

                if (index.IsSlice)
                {
                    stack += context.Load(context.Number(Math.Abs(startIndex)));
                    stack += context.BinaryOperation(TokenType.Subtract);
                    stack += context.Load(context.Number(remaining));
                }
                else
                {
                    stack += context.Load(context.Number(Math.Abs(startIndex)));
                }

                stack += context.BinaryOperation(TokenType.GreaterThanOrEqual);
                stack += context.JumpTrue(destruct);
                stack += context.Drop();
                stack += index.IsSlice ? context.NewArray(0) : context.LoadUndefined();
                stack += context.Jump(assign);

                stack += context.Bind(destruct);
                stack += context.Load(context.Number(startIndex));

                if (index.IsSlice)
                {
                    startIndex = -remaining;

                    stack += context.Load(context.Number(startIndex - 1));
                    stack += context.LoadUndefined();
                    stack += context.Slice();
                }
                else
                {
                    stack += context.LoadArray();
                    startIndex++;
                }

                stack += context.Bind(assign);

                if (global)
                {
                    stack += context.LoadGlobal();
                    stack += context.StoreField(context.String(index.Name));
                }
                else
                {
                    if (!context.DefineIdentifier(index.Name, IsReadOnly))
                        throw new MondCompilerException(this, CompilerError.IdentifierAlreadyDefined, index.Name);

                    stack += context.Store(context.Identifier(index.Name));
                }

                i++;
            }

            stack += context.Drop();

            CheckStack(stack, 0);
            return -1;
        }

        public override Expression Simplify()
        {
            Initializer = Initializer?.Simplify();
            return this;
        }

        public override void SetParent(Expression parent)
        {
            base.SetParent(parent);

            Initializer?.SetParent(this);
        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}