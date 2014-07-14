﻿using System;

namespace Mond.Compiler.Expressions
{
    class StringExpression : Expression, IConstantExpression
    {
        public string Value { get; private set; }

        public StringExpression(Token token, string value)
            : base(token.FileName, token.Line)
        {
            Value = value;
        }

        public override void Print(int indent)
        {
            var indentStr = new string(' ', indent);
            Console.Write(indentStr);
            Console.WriteLine("string: \"{0}\"", Value);
        }

        public override int Compile(CompilerContext context)
        {
            context.Line(FileName, Line);

            context.Load(context.String(Value));
            return 1;
        }

        public override Expression Simplify()
        {
            return this;
        }
    }
}