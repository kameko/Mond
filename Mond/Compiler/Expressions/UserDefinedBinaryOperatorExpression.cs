﻿using System;
using System.Collections.Generic;

namespace Mond.Compiler.Expressions
{
    class UserDefinedBinaryOperatorExpression : Expression
    {
        public string Operator { get; private set; }
        public Expression Left { get; private set; }
        public Expression Right { get; private set; }

        public UserDefinedBinaryOperatorExpression(Token token, Expression left, Expression right, string @operator)
            : base(token)
        {
            Left = left;
            Right = right;
            Operator = @operator;
        }

        public override int Compile(FunctionContext context)
        {
            var stack = 0;

            stack += Left.Compile(context);
            stack += Right.Compile(context);
            stack += context.LoadGlobal();
            stack += context.LoadField(context.String("__ops"));
            stack += context.LoadField(context.String(Operator));
            stack += context.Call(2, new List<ImmediateOperand>());

            CheckStack(stack, 1);
            return stack;
        }

        public override Expression Simplify()
        {
            Left = Left.Simplify();
            Right = Right.Simplify();

            return this;
        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
