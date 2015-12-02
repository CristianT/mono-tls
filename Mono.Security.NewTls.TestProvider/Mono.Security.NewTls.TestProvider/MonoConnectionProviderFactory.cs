﻿//
// MonoConnectionProviderFactory.cs
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
using Xamarin.AsyncTests;
using Xamarin.WebTests.ConnectionFramework;
using Xamarin.WebTests.Providers;
using Xamarin.WebTests.Server;

using MSI = Mono.Security.Interface;
using Mono.Security.Providers.NewTls;
using Mono.Security.Providers.OldTls;

namespace Mono.Security.NewTls.TestProvider
{
	using MonoConnectionFramework;
	using TestFramework;

	class MonoConnectionProviderFactory : ConnectionProviderFactory
	{
		readonly MSI.MonoTlsProvider newTlsProvider;
		readonly MSI.MonoTlsProvider oldTlsProvider;
		readonly DotNetSslStreamProvider dotNetStreamProvider;
		readonly DotNetConnectionProvider dotNetConnectionProvider;
		readonly MonoConnectionProvider newTlsConnectionProvider;
		readonly MonoConnectionProvider oldTlsConnectionProvider;
		readonly MonoConnectionProvider monoWithNewTlsConnectionProvider;
		#if !__MOBILE__
		readonly OpenSslConnectionProvider openSslConnectionProvider;
		#endif
		readonly ManualConnectionProvider manualConnectionProvider;
		readonly MonoProviderExtensions monoExtensions;

		const ConnectionProviderFlags DefaultFlags = ConnectionProviderFlags.SupportsSslStream | ConnectionProviderFlags.SupportsHttp;
		const ConnectionProviderFlags NewTlsFlags = DefaultFlags | ConnectionProviderFlags.SupportsTls12 | ConnectionProviderFlags.SupportsAeadCiphers | ConnectionProviderFlags.SupportsEcDheCiphers;

		internal MonoConnectionProviderFactory ()
		{
			dotNetStreamProvider = new DotNetSslStreamProvider ();
			dotNetConnectionProvider = new DotNetConnectionProvider (this, ConnectionProviderType.DotNet, dotNetStreamProvider);
			Install (dotNetConnectionProvider);

			newTlsProvider = DependencyInjector.Get<NewTlsProvider> ();
			monoExtensions = new MonoProviderExtensions (newTlsProvider);
			newTlsConnectionProvider = new MonoConnectionProvider (this, ConnectionProviderType.NewTLS, NewTlsFlags, newTlsProvider, monoExtensions);
			Install (newTlsConnectionProvider);

			oldTlsProvider = DependencyInjector.Get<OldTlsProvider> ();
			oldTlsConnectionProvider = new MonoConnectionProvider (this, ConnectionProviderType.MonoWithOldTLS, DefaultFlags, oldTlsProvider, null);
			Install (oldTlsConnectionProvider);

			monoWithNewTlsConnectionProvider = new MonoConnectionProvider (this, ConnectionProviderType.MonoWithNewTLS, NewTlsFlags, newTlsProvider, monoExtensions);
			Install (monoWithNewTlsConnectionProvider);

			#if !__MOBILE__
			openSslConnectionProvider = new OpenSslConnectionProvider (this);
			Install (openSslConnectionProvider);
			#endif

			manualConnectionProvider = new ManualConnectionProvider (this, ConnectionProviderFlags.IsExplicit);
			Install (manualConnectionProvider);
		}

		public override ISslStreamProvider DefaultSslStreamProvider {
			get { return newTlsConnectionProvider; }
		}
	}
}

