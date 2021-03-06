﻿//
// CipherInstrumentTestRunner.cs
//
// Author:
//       Martin Baulig <martin.baulig@xamarin.com>
//
// Copyright (c) 2015 Xamarin, Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Mono.Security.Interface;
using Xamarin.AsyncTests;
using Xamarin.AsyncTests.Constraints;
using Xamarin.WebTests.ConnectionFramework;
using Xamarin.WebTests.MonoTestFramework;
using Xamarin.WebTests.Resources;

namespace Mono.Security.NewTls.TestFramework
{
	using TestFeatures;

	[CipherInstrumentTestRunner]
	public class CipherInstrumentTestRunner : InstrumentationTestRunner
	{
		new public CipherInstrumentParameters Parameters {
			get { return (CipherInstrumentParameters)base.Parameters; }
		}

		public CipherInstrumentType Type {
			get { return Parameters.Type; }
		}

		public CipherInstrumentTestRunner (IServer server, IClient client, InstrumentationConnectionProvider provider, CipherInstrumentParameters parameters)
			: base (server, client, provider, parameters)
		{
		}

		protected override MonoConnectionHandler CreateConnectionHandler ()
		{
			return new DefaultMonoConnectionHandler (this);
		}

		public override Instrumentation CreateInstrument (TestContext ctx, MonoTlsSettings settings)
		{
			return null;
		}

		public static IEnumerable<CipherInstrumentParameters> GetParameters (TestContext ctx, InstrumentationCategory category)
		{
			switch (category) {
			case InstrumentationCategory.SelectClientCipher:
				return SelectClientCipherTypes.Select (t => Create (ctx, category, t));

			case InstrumentationCategory.SelectServerCipher:
				return SelectServerCipherTypes.Select (t => Create (ctx, category, t));

			case InstrumentationCategory.SelectCipher:
				return ConnectionTypes.Select (t => Create (ctx, category, t));

			default:
				ctx.AssertFail ("Unsupported instrumentation category: '{0}'.", category);
				return null;
			}
		}

		internal static readonly CipherInstrumentType[] ConnectionTypes = {
			CipherInstrumentType.InvalidCipher
		};

		internal static readonly CipherInstrumentType[] SelectClientCipherTypes = {
			CipherInstrumentType.SelectClientCipher
		};

		internal static readonly CipherInstrumentType[] SelectServerCipherTypes = {
			CipherInstrumentType.SelectServerCipher
		};

		static IEnumerable<CipherInstrumentParameters> SelectAllCiphers (Func<ProtocolVersions,CipherSuiteCode,CipherInstrumentParameters> func)
		{
			foreach (var cipher in CipherList.CiphersTls10)
				yield return func (ProtocolVersions.Tls10, cipher);
			foreach (var cipher in CipherList.CiphersTls10)
				yield return func (ProtocolVersions.Tls11, cipher);
			foreach (var cipher in CipherList.CiphersTls12)
				yield return func (ProtocolVersions.Tls12, cipher);
		}

		static CipherInstrumentParameters CreateParameters (InstrumentationCategory category, CipherInstrumentType type, params object[] args)
		{
			var sb = new StringBuilder ();
			sb.Append (type);
			foreach (var arg in args) {
				sb.AppendFormat (":{0}", arg);
			}
			var name = sb.ToString ();

			return new CipherInstrumentParameters (category, type, name, ResourceManager.SelfSignedServerCertificate) {
				ClientCertificateValidator = AcceptAnyCertificate, ServerCertificateValidator = AcceptAnyCertificate
			};
		}

		static CipherInstrumentParameters Create (TestContext ctx, InstrumentationCategory category, CipherInstrumentType type)
		{
			var parameters = CreateParameters (category, type);

			switch (type) {
			case CipherInstrumentType.SelectClientCipher:
				parameters.ProtocolVersion = ctx.GetParameter<ProtocolVersions> ();
				parameters.ClientCiphers = new CipherSuiteCode[] { ctx.GetParameter<CipherSuiteCode> () };
				parameters.ValidateCipherList = true;
				break;

			case CipherInstrumentType.SelectServerCipher:
				parameters.ProtocolVersion = ctx.GetParameter<ProtocolVersions> ();
				parameters.ServerCiphers = new CipherSuiteCode[] { ctx.GetParameter<CipherSuiteCode> () };
				parameters.ValidateCipherList = true;
				break;

			case CipherInstrumentType.InvalidCipher:
				parameters.ServerCiphers = new CipherSuiteCode[] { CipherSuiteCode.TLS_DHE_RSA_WITH_AES_128_CBC_SHA };
				parameters.ClientCiphers = new CipherSuiteCode[] { CipherSuiteCode.TLS_DHE_RSA_WITH_AES_256_CBC_SHA256 };
				parameters.ProtocolVersion = ProtocolVersions.Tls12;
				parameters.ExpectServerAlert = AlertDescription.HandshakeFailure;
				break;

			default:
				ctx.AssertFail ("Unsupported cipher instrument: '{0}'.", type);
				break;
			}

			return parameters;
		}
	}
}

