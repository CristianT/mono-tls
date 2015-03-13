﻿using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Mono.Security.NewTls;
using Mono.Security.NewTls.Cipher;
using Mono.Security.NewTls.TestFramework;
using Xamarin.AsyncTests;
using System.Security.Cryptography;

namespace Mono.Security.NewTls.Console
{
	public class NativeCryptoProvider : IHashTestHost
	{
		RandomNumberGenerator rng = RandomNumberGenerator.Create ();

		public byte[] GetRandomBytes (int count)
		{
			var data = new byte [count];
			rng.GetBytes (data);
			return data;
		}

		public Task Initialize (TestContext ctx, CancellationToken cancellationToken)
		{
			return Task.FromResult<object> (null);
		}

		public Task PreRun (TestContext ctx, CancellationToken cancellationToken)
		{
			return Task.FromResult<object> (null);
		}

		public Task PostRun (TestContext ctx, CancellationToken cancellationToken)
		{
			return Task.FromResult<object> (null);
		}

		public Task Destroy (TestContext ctx, CancellationToken cancellationToken)
		{
			return Task.FromResult<object> (null);
		}

		static NativeCryptoProvider ()
		{
			native_crypto_test_init ();
		}

		[DllImport (NativeOpenSsl.DLL)]
		extern static void native_crypto_test_init ();

		[DllImport (NativeOpenSsl.DLL)]
		extern static int native_crypto_test_PRF (
			int digest_mask,
			byte[] seed1, int seed1_len,
			byte[] seed2, int seed2_len,
			byte[] seed3, int seed3_len,
			byte[] seed4, int seed4_len,
			byte[] seed5, int seed5_len,
			byte[] sec, int slen,
			byte[] out1, byte[] out2, int olen);

		[DllImport (NativeOpenSsl.DLL)]
		extern static int native_crypto_test_digest (
			int digest_mask, byte[] data, int data_len, byte[] output, int length);

		[DllImport (NativeOpenSsl.DLL)]
		extern static int native_crypto_test_get_prf_algorithm_sha256 ();

		[DllImport (NativeOpenSsl.DLL)]
		extern static int native_crypto_test_get_prf_algorithm_sha384 ();

		int GetAlgorithm (HandshakeHashType algorithm)
		{
			switch (algorithm) {
			case HandshakeHashType.SHA256:
				return native_crypto_test_get_prf_algorithm_sha256 ();
			case HandshakeHashType.SHA384:
				return native_crypto_test_get_prf_algorithm_sha384 ();
			default:
				throw new NotSupportedException ();
			}
		}

		public byte[] TestPRF (HandshakeHashType algorithm, byte[] secret, string seed, byte[] data, int length)
		{
			var digest_mask = GetAlgorithm (algorithm);

			var output = new byte [length];
			var temp = new byte [length];

			var seedBytes = Encoding.ASCII.GetBytes (seed);
			int ret = native_crypto_test_PRF (digest_mask, seedBytes, seedBytes.Length, data, data.Length,
				null, 0, null, 0, null, 0, secret, secret.Length, output, temp, length);
			if (ret != 1)
				throw new InvalidOperationException ();
			return output;
		}

		public byte[] TestDigest (HandshakeHashType algorithm, byte[] data)
		{
			var digest_mask = GetAlgorithm (algorithm);

			var output = new byte [200];
			var ret = native_crypto_test_digest (digest_mask, data, data.Length, output, output.Length);
			if (ret <= 0)
				throw new InvalidOperationException ();

			var buffer = new byte [ret];
			Buffer.BlockCopy (output, 0, buffer, 0, ret);
			return buffer;
		}

		public bool SupportsEncryption {
			get { return false; }
		}
	}
}
