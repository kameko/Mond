﻿using System;
using System.Collections.Generic;
using System.Text;
using Mond.Compiler;

namespace Mond.VirtualMachine
{
    class Machine
    {
        private const int MaxCallDepth = 250;
        private const int InitialEvalStackCapacity = 500;

        private readonly MondState _state;

        private readonly Stack<ReturnAddress> _callStack;
        private readonly Stack<Frame> _localStack;
        private readonly Stack<MondValue> _evalStack;

        public MondValue Global;

        public Machine(MondState state)
        {
            _state = state;

            _callStack = new Stack<ReturnAddress>(MaxCallDepth);
            _localStack = new Stack<Frame>(MaxCallDepth);
            _evalStack = new Stack<MondValue>(InitialEvalStackCapacity);

            Global = new MondValue(MondValueType.Object);
        }

        public MondValue Load(MondProgram program)
        {
            if (program == null)
                throw new ArgumentNullException("program");

            var closure = new MondValue(new Closure(program, 0, null, null));
            return Call(closure);
        }

        public MondValue Call(MondValue closure, params MondValue[] arguments)
        {
            if (closure.Type != MondValueType.Closure)
                throw new MondRuntimeException(RuntimeError.ValueNotCallable, closure.Type);

            var closureValue = closure.ClosureValue;

            if (closureValue.Type == ClosureType.Mond)
            {
                var argFrame = closureValue.Arguments;
                if (argFrame == null)
                    argFrame = new Frame(0, null, arguments.Length);
                else
                    argFrame = new Frame(argFrame.Depth + 1, argFrame, arguments.Length);

                for (var i = 0; i < arguments.Length; i++)
                {
                    argFrame.Values[i] = arguments[i];
                }

                _callStack.Push(new ReturnAddress(closureValue.Program, closureValue.Address, argFrame));

                if (closureValue.Locals != null)
                    _localStack.Push(closureValue.Locals);
            }
            else if (closureValue.Type == ClosureType.Native)
            {
                return closureValue.NativeFunction(_state, arguments);
            }
            else
            {
                throw new NotSupportedException();
            }

            return Run();
        }

        public MondValue Run()
        {
            var functionAddress = _callStack.Peek();
            var program = functionAddress.Program;
            var code = program.Bytecode;

            var initialCallDepth = _callStack.Count - 1;
            var initialLocalDepth = _localStack.Count;
            var initialEvalDepth = _evalStack.Count;

            var ip = functionAddress.Address;
            var errorIp = 0;

            var args = functionAddress.Arguments;
            Frame locals = null;

            try
            {
                while (true)
                {
                    errorIp = ip;

                    /*if (program.DebugInfo != null)
                    {
                        var line = program.DebugInfo.FindLine(errorIp);
                        if (line.HasValue)
                            Console.WriteLine("{0:X4} line {1,3}: {2}", errorIp, line.Value.LineNumber, (InstructionType)code[ip]);
                        else
                            Console.WriteLine("{0:X4}: {1}", errorIp, (InstructionType)code[ip]);
                    }*/

                    switch (code[ip++])
                    {
                        #region Stack Manipulation
                        case (int)InstructionType.Dup:
                            {
                                _evalStack.Push(_evalStack.Peek());
                                break;
                            }

                        case (int)InstructionType.Drop:
                            {
                                _evalStack.Pop();
                                break;
                            }

                        case (int)InstructionType.Swap:
                            {
                                var value1 = _evalStack.Pop();
                                var value2 = _evalStack.Pop();
                                _evalStack.Push(value1);
                                _evalStack.Push(value2);
                                break;
                            }
                        #endregion

                        #region Constants
                        case (int)InstructionType.LdUndef:
                            {
                                _evalStack.Push(MondValue.Undefined);
                                break;
                            }

                        case (int)InstructionType.LdNull:
                            {
                                _evalStack.Push(MondValue.Null);
                                break;
                            }

                        case (int)InstructionType.LdTrue:
                            {
                                _evalStack.Push(MondValue.True);
                                break;
                            }

                        case (int)InstructionType.LdFalse:
                            {
                                _evalStack.Push(MondValue.False);
                                break;
                            }

                        case (int)InstructionType.LdNum:
                            {
                                var numId = ReadInt32(code, ref ip);
                                _evalStack.Push(program.Numbers[numId]);
                                break;
                            }

                        case (int)InstructionType.LdStr:
                            {
                                var strId = ReadInt32(code, ref ip);
                                _evalStack.Push(program.Strings[strId]);
                                break;
                            }

                        case (int)InstructionType.LdGlobal:
                            {
                                _evalStack.Push(Global);
                                break;
                            }
                        #endregion

                        #region Storables
                        case (int)InstructionType.LdLoc:
                            {
                                var depth = ReadInt32(code, ref ip);
                                var index = ReadInt32(code, ref ip);

                                if (depth < 0)
                                    _evalStack.Push(args.Get(Math.Abs(depth), index));
                                else
                                    _evalStack.Push(locals.Get(depth, index));

                                break;
                            }

                        case (int)InstructionType.StLoc:
                            {
                                var depth = ReadInt32(code, ref ip);
                                var index = ReadInt32(code, ref ip);

                                if (depth < 0)
                                    args.Set(Math.Abs(depth), index, _evalStack.Pop());
                                else
                                    locals.Set(depth, index, _evalStack.Pop());

                                break;
                            }

                        case (int)InstructionType.LdFld:
                            {
                                var obj = _evalStack.Pop();
                                _evalStack.Push(obj[program.Strings[ReadInt32(code, ref ip)]]);
                                break;
                            }

                        case (int)InstructionType.StFld:
                            {
                                var obj = _evalStack.Pop();
                                var value = _evalStack.Pop();

                                obj[program.Strings[ReadInt32(code, ref ip)]] = value;
                                break;
                            }

                        case (int)InstructionType.LdArr:
                            {
                                var index = _evalStack.Pop();
                                var array = _evalStack.Pop();

                                _evalStack.Push(array[index]);
                                break;
                            }

                        case (int)InstructionType.StArr:
                            {
                                var index = _evalStack.Pop();
                                var array = _evalStack.Pop();
                                var value = _evalStack.Pop();

                                array[index] = value;
                                break;
                            }
                        #endregion

                        #region Object Creation
                        case (int)InstructionType.NewObject:
                            {
                                _evalStack.Push(new MondValue(MondValueType.Object));
                                break;
                            }

                        case (int)InstructionType.NewArray:
                            {
                                var count = ReadInt32(code, ref ip);
                                var array = new MondValue(MondValueType.Array);

                                for (var i = 0; i < count; i++)
                                {
                                    array.ArrayValue.Add(default(MondValue));
                                }

                                for (var i = count - 1; i >= 0; i--)
                                {
                                    array.ArrayValue[i] = _evalStack.Pop();
                                }

                                _evalStack.Push(array);
                                break;
                            }
                        #endregion

                        #region Math
                        case (int)InstructionType.Add:
                            {
                                var left = _evalStack.Pop();
                                var right = _evalStack.Pop();
                                _evalStack.Push(left + right);
                                break;
                            }

                        case (int)InstructionType.Sub:
                            {
                                var left = _evalStack.Pop();
                                var right = _evalStack.Pop();
                                _evalStack.Push(left - right);
                                break;
                            }

                        case (int)InstructionType.Mul:
                            {
                                var left = _evalStack.Pop();
                                var right = _evalStack.Pop();
                                _evalStack.Push(left * right);
                                break;
                            }

                        case (int)InstructionType.Div:
                            {
                                var left = _evalStack.Pop();
                                var right = _evalStack.Pop();
                                _evalStack.Push(left / right);
                                break;
                            }

                        case (int)InstructionType.Mod:
                            {
                                var left = _evalStack.Pop();
                                var right = _evalStack.Pop();
                                _evalStack.Push(left % right);
                                break;
                            }

                        case (int)InstructionType.Neg:
                            {
                                _evalStack.Push(-_evalStack.Pop());
                                break;
                            }
                        #endregion

                        #region Logic
                        case (int)InstructionType.Eq:
                            {
                                var left = _evalStack.Pop();
                                var right = _evalStack.Pop();
                                _evalStack.Push(left == right);
                                break;
                            }

                        case (int)InstructionType.Neq:
                            {
                                var left = _evalStack.Pop();
                                var right = _evalStack.Pop();
                                _evalStack.Push(left != right);
                                break;
                            }

                        case (int)InstructionType.Gt:
                            {
                                var left = _evalStack.Pop();
                                var right = _evalStack.Pop();
                                _evalStack.Push(left > right);
                                break;
                            }

                        case (int)InstructionType.Gte:
                            {
                                var left = _evalStack.Pop();
                                var right = _evalStack.Pop();
                                _evalStack.Push(left >= right);
                                break;
                            }

                        case (int)InstructionType.Lt:
                            {
                                var left = _evalStack.Pop();
                                var right = _evalStack.Pop();
                                _evalStack.Push(left < right);
                                break;
                            }

                        case (int)InstructionType.Lte:
                            {
                                var left = _evalStack.Pop();
                                var right = _evalStack.Pop();
                                _evalStack.Push(left <= right);
                                break;
                            }

                        case (int)InstructionType.Not:
                            {
                                _evalStack.Push(!_evalStack.Pop());
                                break;
                            }
                        #endregion

                        #region Functions
                        case (int)InstructionType.Closure:
                            {
                                var address = ReadInt32(code, ref ip);
                                _evalStack.Push(new MondValue(new Closure(program, address, args, locals)));
                                break;
                            }

                        case (int)InstructionType.Call:
                            {
                                var argCount = ReadInt32(code, ref ip);
                                var returnAddress = ip;
                                var closure = _evalStack.Pop();

                                if (closure.Type != MondValueType.Closure)
                                    throw new MondRuntimeException(RuntimeError.ValueNotCallable, closure.Type);

                                var closureValue = closure.ClosureValue;

                                var argFrame = closure.ClosureValue.Arguments;
                                if (argFrame == null)
                                    argFrame = new Frame(1, null, argCount);
                                else
                                    argFrame = new Frame(argFrame.Depth + 1, argFrame, argCount);

                                for (var i = argCount - 1; i >= 0; i--)
                                {
                                    argFrame.Values[i] = _evalStack.Pop();
                                }

                                switch (closureValue.Type)
                                {
                                    case ClosureType.Mond:
                                        if (_callStack.Count >= MaxCallDepth)
                                            throw new MondRuntimeException(RuntimeError.StackOverflow);

                                        _callStack.Push(new ReturnAddress(program, returnAddress, argFrame));
                                        _localStack.Push(closureValue.Locals);
                                        program = closureValue.Program;
                                        code = program.Bytecode;
                                        ip = closureValue.Address;
                                        args = argFrame;
                                        locals = closureValue.Locals;
                                        break;

                                    case ClosureType.Native:
                                        var result = closureValue.NativeFunction(_state, argFrame.Values);
                                        _evalStack.Push(result);
                                        break;

                                    default:
                                        throw new MondRuntimeException(RuntimeError.UnhandledClosureType);
                                }

                                break;
                            }

                        case (int)InstructionType.TailCall:
                            {
                                var argCount = ReadInt32(code, ref ip);
                                var address = ReadInt32(code, ref ip);

                                var returnAddress = _callStack.Pop();
                                var argFrame = returnAddress.Arguments;

                                // copy arguments into frame
                                for (var i = argCount - 1; i >= 0; i--)
                                {
                                    argFrame.Values[i] = _evalStack.Pop();
                                }

                                // clear other arguments
                                for (var i = argCount; i < argFrame.Values.Length; i++)
                                {
                                    argFrame.Values[i] = MondValue.Undefined;
                                }

                                // get rid of old locals
                                _localStack.Push(_localStack.Pop().Previous);

                                _callStack.Push(new ReturnAddress(returnAddress.Program, returnAddress.Address, argFrame));

                                ip = address;
                                break;
                            }

                        case (int)InstructionType.Enter:
                            {
                                var localCount = ReadInt32(code, ref ip);
                                Frame frame;

                                if (_localStack.Count > 0)
                                {
                                    frame = _localStack.Pop();
                                    frame = new Frame(frame.Depth + 1, frame, localCount);
                                }
                                else
                                {
                                    frame = new Frame(0, null, localCount);
                                }

                                _localStack.Push(frame);
                                locals = frame;
                                break;
                            }

                        case (int)InstructionType.Ret:
                            {
                                var returnAddress = _callStack.Pop();
                                _localStack.Pop();

                                program = returnAddress.Program;
                                code = program.Bytecode;
                                ip = returnAddress.Address;

                                args = _callStack.Count > 0 ? _callStack.Peek().Arguments : null;
                                locals = _localStack.Count > 0 ? _localStack.Peek() : null;

                                if (_callStack.Count == initialCallDepth)
                                    return _evalStack.Pop();

                                break;
                            }

                        case (int)InstructionType.VarArgs:
                            {
                                var fixedCount = ReadInt32(code, ref ip);
                                var varArgs = new MondValue(MondValueType.Array);

                                for (var i = fixedCount; i < args.Values.Length; i++)
                                {
                                    varArgs.ArrayValue.Add(args.Values[i]);
                                }

                                args.Set(args.Depth, fixedCount, varArgs);
                                break;
                            }

                        case (int)InstructionType.JmpTable:
                            {
                                var start = ReadInt32(code, ref ip);
                                var count = ReadInt32(code, ref ip);

                                var endIp = ip + count * 4;

                                var value = _evalStack.Pop();
                                if (value.Type == MondValueType.Number)
                                {
                                    var number = (double)value;
                                    var numberInt = (int)Math.Truncate(number);

                                    if (number >= start && number < start + count &&
                                        Math.Abs(number - numberInt) <= double.Epsilon)
                                    {
                                        ip += (numberInt - start) * 4;
                                        ip = ReadInt32(code, ref ip);
                                        break;
                                    }
                                }

                                ip = endIp;
                                break;
                            }
                        #endregion

                        #region Branching
                        case (int)InstructionType.Jmp:
                            {
                                var address = ReadInt32(code, ref ip);
                                ip = address;
                                break;
                            }

                        case (int)InstructionType.JmpTrueP:
                            {
                                var address = ReadInt32(code, ref ip);

                                if (_evalStack.Peek())
                                    ip = address;

                                break;
                            }

                        case (int)InstructionType.JmpFalseP:
                            {
                                var address = ReadInt32(code, ref ip);

                                if (!_evalStack.Peek())
                                    ip = address;

                                break;
                            }

                        case (int)InstructionType.JmpTrue:
                            {
                                var address = ReadInt32(code, ref ip);

                                if (_evalStack.Pop())
                                    ip = address;

                                break;
                            }

                        case (int)InstructionType.JmpFalse:
                            {
                                var address = ReadInt32(code, ref ip);

                                if (!_evalStack.Pop())
                                    ip = address;

                                break;
                            }
                        #endregion

                        default:
                            throw new MondRuntimeException(RuntimeError.UnhandledOpcode);
                    }
                }
            }
            catch (Exception e)
            {
                var errorBuilder = new StringBuilder();

                errorBuilder.AppendLine(e.Message.Trim());

                var runtimeException = e as MondRuntimeException;
                if (runtimeException != null && runtimeException.HasStackTrace)
                    errorBuilder.AppendLine("[... native ...]");
                else
                    errorBuilder.AppendLine();

                errorBuilder.AppendLine(GetAddressDebugInfo(program, errorIp));

                while (_callStack.Count > initialCallDepth + 1)
                {
                    var returnAddress = _callStack.Pop();

                    errorBuilder.AppendLine(GetAddressDebugInfo(returnAddress.Program, returnAddress.Address));
                }

                _callStack.Pop();

                while (_localStack.Count > initialLocalDepth)
                {
                    _localStack.Pop();
                }

                while (_evalStack.Count > initialEvalDepth)
                {
                    _evalStack.Pop();
                }

                throw new MondRuntimeException(errorBuilder.ToString(), e, true);
            }
        }

        private static int ReadInt32(byte[] buffer, ref int offset)
        {
            return buffer[offset++] <<  0 |
                   buffer[offset++] <<  8 |
                   buffer[offset++] << 16 |
                   buffer[offset++] << 24;
        }

        private static string GetAddressDebugInfo(MondProgram program, int address)
        {
            if (program.DebugInfo != null)
            {
                var func = program.DebugInfo.FindFunction(address);
                var line = program.DebugInfo.FindLine(address);

                if (func.HasValue && line.HasValue)
                {
                    var prefix = "";
                    var funcName = program.Strings[func.Value.Name];
                    var fileName = program.Strings[line.Value.FileName];

                    if (!string.IsNullOrEmpty(funcName))
                        prefix = string.Format("at {0} ", funcName);

                    return string.Format("{0}in {1}: line {2}", prefix, fileName, line.Value.LineNumber);
                }
            }

            return address.ToString("X8");
        }
    }
}
