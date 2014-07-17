﻿using System;

namespace Mond.Compiler.Expressions
{
    class PrefixOperatorExpression : Expression
    {
        public TokenType Operation { get; private set; }
        public Expression Right { get; private set; }

        public PrefixOperatorExpression(Token token, Expression right)
            : base(token.FileName, token.Line)
        {
            Operation = token.Type;
            Right = right;
        }

        public override void Print(int indent)
        {
            var indentStr = new string(' ', indent);

            Console.Write(indentStr);
            Console.WriteLine("Prefix {0}", Operation);

            Right.Print(indent + 1);
        }

        public override int Compile(FunctionContext context)
        {
            context.Line(FileName, Line);

            var stack = 0;
            var isAssignment = false;
            var needResult = !(Parent is BlockExpression);
            
            switch (Operation)
            {
                case TokenType.Increment:
                    stack += context.Load(context.Number(1));
                    stack += Right.Compile(context);
                    stack += context.BinaryOperation(TokenType.Add);
                    isAssignment = true;
                    break;

                case TokenType.Decrement:
                    stack += context.Load(context.Number(1));
                    stack += Right.Compile(context);
                    stack += context.BinaryOperation(TokenType.Subtract);
                    isAssignment = true;
                    break;

                case TokenType.Subtract:
                case TokenType.LogicalNot:
                    stack += Right.Compile(context);
                    stack += context.UnaryOperation(Operation);
                    break;

                default:
                    throw new NotSupportedException();
            }

            if (isAssignment)
            {
                var storable = Right as IStorableExpression;
                if (storable == null)
                    throw new MondCompilerException(FileName, Line, "The left-hand side of an assignment must be storable"); // TODO: better message

                if (needResult)
                    stack += context.Dup();

                stack += storable.CompileStore(context);
            }

            CheckStack(stack, needResult ? 1 : 0);
            return stack;
        }

        public override Expression Simplify()
        {
            Right = Right.Simplify();

            if (Operation == TokenType.Subtract)
            {
                var number = Right as NumberExpression;
                if (number != null)
                {
                    var token = new Token(Right.FileName, Right.Line, TokenType.Number, null);
                    Right = new NumberExpression(token, -number.Value);
                }
            }

            return this;
        }

        public override void SetParent(Expression parent)
        {
            base.SetParent(parent);

            Right.SetParent(this);
        }
    }
}
