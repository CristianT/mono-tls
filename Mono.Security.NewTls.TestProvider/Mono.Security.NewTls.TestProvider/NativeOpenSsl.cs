﻿//
// NativeOpenSsl.cs
//
// Author:
//       Martin Baulig <martin.baulig@xamarin.com>
//
// Copyright (c) 2014 Xamarin Inc. (http://www.xamarin.com)
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
using System.IO;
using System.Net;
using System.Net.Security;
using System.Threading;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Mono.Security.NewTls;
using Mono.Security.Interface;

namespace Mono.Security.NewTls.TestProvider
{
	public class NativeOpenSsl : Stream
	{
		bool isServer;
		bool enableDebugging;
		OpenSslHandle handle;
		CertificateHandle certificate;
		PrivateKeyHandle privateKey;
		DebugCallback debug_callback;
		MessageCallback message_callback;
		VerifyCallback verify_callback;
		RemoteValidationCallback managed_cert_callback;
		NativeOpenSslProtocol protocol;
		ShutdownState shutdownState;
		TlsException lastAlert;
		int lockReadState;
		int lockWriteState;

		readonly Func<bool,bool> shutdownHandler;
		readonly Func<byte[],int,int,int> readHandler;
		readonly Action<byte[],int,int> writeHandler;

		public delegate bool RemoteValidationCallback (bool ok, X509Certificate certificate);

		class OpenSslHandle : SafeHandle
		{
			OpenSslHandle ()
				: base (IntPtr.Zero, true)
			{
			}

			public override bool IsInvalid {
				get { return handle == IntPtr.Zero; }
			}

			protected override bool ReleaseHandle ()
			{
				native_openssl_destroy (handle);
				return true;
			}

			[DllImport (DLL)]
			extern static void native_openssl_destroy (IntPtr handle);
		}

		class CertificateHandle : SafeHandle
		{
			CertificateHandle ()
				: base (IntPtr.Zero, true)
			{
			}

			public override bool IsInvalid {
				get { return handle == IntPtr.Zero; }
			}

			protected override bool ReleaseHandle ()
			{
				native_openssl_free_certificate (handle);
				return true;
			}

			[DllImport (DLL)]
			extern static void native_openssl_free_certificate (IntPtr handle);
		}

		class PrivateKeyHandle : SafeHandle
		{
			PrivateKeyHandle ()
				: base (IntPtr.Zero, true)
			{
			}

			public override bool IsInvalid {
				get { return handle == IntPtr.Zero; }
			}

			protected override bool ReleaseHandle ()
			{
				native_openssl_free_private_key (handle);
				return true;
			}

			[DllImport (DLL)]
			extern static void native_openssl_free_private_key (IntPtr handle);
		}

		void Debug (string message, params object[] args)
		{
			if (enableDebugging)
				DebugHelper.WriteLine ("NativeOpenSsl({0}): {1}", isServer ? "server" : "client", string.Format (message, args));
		}

		void Debug (string message, byte[] buffer)
		{
			if (enableDebugging)
				DebugHelper.WriteBuffer (string.Format ("NativeOpenSsl({0}): {1}", isServer ? "server" : "client", message), buffer);
		}

		void OnDebugCallback (bool write, byte[] buffer)
		{
			Debug (write ? "WRITE" : "READ", buffer);
		}

		void OnDebugCallback (int cmd, IntPtr ptr, int size, int ret)
		{
			try {
				Debug ("DEBUG CALLBACK: {0:x} {1:x} {2:x} {3:x}", cmd, ptr.ToInt32 (), size, ret);
				var buffer = new byte [size];
				Marshal.Copy (ptr, buffer, 0, size);
				OnDebugCallback ((cmd & 0x0f) == 0x03, buffer);
			} catch (Exception ex) {
				Debug ("EXCEPTION IN DEBUG CALLBACK: {0}", ex);
			}
		}

		void OnMessageCallback (int write_p, int version, int content_type, IntPtr buf, int size)
		{
			try {
				if (enableDebugging)
					Debug ("MESSAGE CALLBACK: {0} {1:x} {2:x} {3:x} {4:x}", write_p, version, content_type, size, buf.ToInt32 ());

				var buffer = new byte [size];
				Marshal.Copy (buf, buffer, 0, size);
				if (enableDebugging)
					Debug ("MESSAGE", buffer);

				if ((ContentType)content_type == ContentType.Alert) {
					var alert = new Alert ((AlertLevel)buffer [0], (AlertDescription)buffer [1]);
					if (enableDebugging || !alert.IsWarning)
						Debug ("ALERT: {0}", alert);
					lastAlert = new TlsException (alert);
				}
			} catch (Exception ex) {
				Debug ("EXCEPTION IN MESSAGE CALLBACK: {0}", ex);
			}
		}

		delegate void DebugCallback (int cmd, IntPtr ptr, int size, int ret);

		delegate void MessageCallback (int write_p, int version, int content_type, IntPtr buf, int size);

		void CheckError (int ret)
		{
			if (ret != 0) {
				if (lastAlert != null)
					throw lastAlert;
				throw new NativeOpenSslException ((NativeOpenSslError)ret);
			}
		}

		internal const string DLL = "NativeOpenSsl";

		[DllImport (DLL)]
		extern static OpenSslHandle native_openssl_initialize (int debug, NativeOpenSslProtocol protocol, DebugCallback debug_callback, MessageCallback message_callback);

		[DllImport (DLL)]
		extern static int native_openssl_set_dh_params (OpenSslHandle handle, byte[] p, int p_len, byte[] g, int b_len);

		[DllImport (DLL)]
		extern static int native_openssl_set_named_curve (OpenSslHandle handle, string curve_name);

		[DllImport (DLL)]
		extern static int native_openssl_create_context (OpenSslHandle handle, bool client);

		[DllImport (DLL)]
		extern static int native_openssl_create_connection (OpenSslHandle handle);

		[DllImport (DLL)]
		extern static int native_openssl_close (OpenSslHandle handle);

		[DllImport (DLL)]
		extern static int native_openssl_connect (OpenSslHandle handle, byte[] ip, int port);

		[DllImport (DLL)]
		extern static int native_openssl_bind (OpenSslHandle handle, byte[] ip, int port);

		[DllImport (DLL)]
		extern static int native_openssl_accept (OpenSslHandle handle);

		[DllImport (DLL)]
		extern static int native_openssl_write (OpenSslHandle handle, byte[] buffer, int offset, int size);

		[DllImport (DLL)]
		extern static int native_openssl_read (OpenSslHandle handle, byte[] buffer, int offset, int size);

		[DllImport (DLL)]
		extern static CertificateHandle native_openssl_load_certificate_from_pem (OpenSslHandle handle, byte[] buffer, int len);

		[DllImport (DLL)]
		extern static PrivateKeyHandle native_openssl_load_private_key_from_pem (OpenSslHandle handle, byte[] buffer, int len);

		[DllImport (DLL)]
		extern static int native_openssl_load_certificate_from_pkcs12 (
			OpenSslHandle handle, byte[] buffer, int len,
			[MarshalAs (UnmanagedType.LPStr)] string password, int passlen,
			out CertificateHandle certificate, out PrivateKeyHandle privateKey);

		[DllImport (DLL)]
		extern static int native_openssl_set_certificate (
			OpenSslHandle handle, CertificateHandle certificate, PrivateKeyHandle privateKey);

		[DllImport (DLL)]
		extern static void native_openssl_set_certificate_verify (OpenSslHandle handle, int mode, VerifyCallback verify_cb, CertificateVerifyCallback cert_cb, int depth);

		delegate int VerifyCallback (int ok, IntPtr store_ctx);

		delegate int CertificateVerifyCallback (IntPtr store_ctx, IntPtr cert);

		[DllImport (DLL)]
		extern static int X509_STORE_CTX_get_error (IntPtr store);

		[DllImport (DLL)]
		extern static IntPtr X509_STORE_CTX_get_current_cert (IntPtr store);

		[DllImport (DLL)]
		extern static IntPtr BIO_s_mem ();

		[DllImport (DLL)]
		extern static IntPtr BIO_new (IntPtr method);

		[DllImport (DLL)]
		extern static IntPtr BIO_free (IntPtr bio);

		[DllImport (DLL)]
		extern static int native_openssl_BIO_get_mem_data (IntPtr bio, out IntPtr data);

		[DllImport (DLL)]
		extern static int PEM_write_bio_X509 (IntPtr bio, IntPtr cert);

		[DllImport (DLL)]
		extern static int native_openssl_shutdown (OpenSslHandle handle);

		[DllImport (DLL)]
		extern static short native_openssl_get_current_cipher (OpenSslHandle handle);

		[DllImport (DLL)]
		extern static int native_openssl_set_cipher_list (OpenSslHandle handle, byte[] ciphers, int count);

		public override bool CanRead {
			get { return true; }
		}

		public override bool CanWrite {
			get { return true; }
		}

		public override bool CanSeek {
			get { return false; }
		}

		static Exception GetConcurrentOperationEx ()
		{
			return new InvalidOperationException ("Attempted concurrent read/write operation.");
		}

		public void SetDhParams (byte[] p, byte[] g)
		{
			var ret = native_openssl_set_dh_params (handle, p, p.Length, g, g.Length);
			if (ret != 0)
				throw new IOException ("native_openssl_set_dh_params() failed.");
		}

		public void SetNamedCurve (string curve)
		{
			var ret = native_openssl_set_named_curve (handle, curve);
			CheckError (ret);
		}

		public override void Write (byte[] buffer, int offset, int size)
		{
			if (Interlocked.CompareExchange (ref lockWriteState, 1, 0) != 0)
				throw GetConcurrentOperationEx ();

			try {
				Write_internal (buffer, offset, size);
			} finally {
				lockWriteState = 0;
			}
		}

		void Write_internal (byte[] buffer, int offset, int size)
		{
			Debug ("WRITE: {0}", size);
			var ret = native_openssl_write (handle, buffer, offset, size);
			Debug ("WRITE DONE: {0}", ret);
			if (ret != size)
				throw new IOException ("Write failed.");
		}

		public override IAsyncResult BeginWrite (byte[] buffer, int offset, int count, AsyncCallback callback, object state)
		{
			if (Interlocked.CompareExchange (ref lockWriteState, 1, 0) != 0)
				throw GetConcurrentOperationEx ();

			try {
				return writeHandler.BeginInvoke (buffer, offset, count, callback, state);
			} catch {
				lockWriteState = 0;
				throw;
			}
		}

		public override void EndWrite (IAsyncResult asyncResult)
		{
			try {
				writeHandler.EndInvoke (asyncResult);
			} finally {
				lockWriteState = 0;
			}
		}

		public override int Read (byte[] buffer, int offset, int size)
		{
			if (Interlocked.CompareExchange (ref lockReadState, 1, 0) != 0)
				throw GetConcurrentOperationEx ();

			try {
				return Read_internal (buffer, offset, size);
			} finally {
				lockReadState = 0;
			}
		}

		int Read_internal (byte[] buffer, int offset, int size)
		{
			Debug ("READ: {0}", size);
			var ret = native_openssl_read (handle, buffer, offset, size);
			Debug ("READ DONE: {0}", ret);
			return ret;
		}

		public override IAsyncResult BeginRead (byte[] buffer, int offset, int count, AsyncCallback callback, object state)
		{
			if (Interlocked.CompareExchange (ref lockReadState, 1, 0) != 0)
				throw GetConcurrentOperationEx ();

			try {
				return readHandler.BeginInvoke (buffer, offset, count, callback, state);
			} catch {
				lockReadState = 0;
				throw;
			}
		}

		public override int EndRead (IAsyncResult asyncResult)
		{
			try {
				return readHandler.EndInvoke (asyncResult);
			} finally {
				lockReadState = 0;
			}
		}

		public override void Flush ()
		{
			;
		}

		public override long Length {
			get { throw new InvalidOperationException (); }
		}

		public override long Position {
			get { throw new InvalidOperationException (); }
			set { throw new InvalidOperationException (); }
		}

		public override long Seek (long offset, SeekOrigin origin)
		{
			throw new InvalidOperationException ();
		}

		public override void SetLength (long value)
		{
			throw new InvalidOperationException ();
		}

		public NativeOpenSslProtocol Protocol {
			get { return protocol; }
		}

		public NativeOpenSsl (bool isServer, bool debug, NativeOpenSslProtocol protocol)
		{
			this.isServer = isServer;
			this.enableDebugging = debug;
			this.protocol = protocol;

			readHandler = Read_internal;
			writeHandler = Write_internal;
			shutdownHandler = Shutdown_internal;

			if (debug)
				debug_callback = new DebugCallback (OnDebugCallback);

			message_callback = new MessageCallback (OnMessageCallback);

			handle = native_openssl_initialize (debug ? 1 : 0, protocol, debug_callback, message_callback);
			if (handle.IsInvalid)
				throw new InvalidOperationException ("Handle invalid.");

			var ret = native_openssl_create_context (handle, !isServer);
			CheckError (ret);
		}

		public void Connect (IPEndPoint endpoint)
		{
			if (isServer)
				throw new InvalidOperationException ();

			var ret = native_openssl_create_connection (handle);
			CheckError (ret);

			ret = native_openssl_connect (handle, endpoint.Address.GetAddressBytes (), endpoint.Port);
			CheckError (ret);
		}

		public void Bind (IPEndPoint endpoint)
		{
			if (!isServer)
				throw new InvalidOperationException ();

			var ret = native_openssl_create_connection (handle);
			CheckError (ret);

			ret = native_openssl_bind (handle, endpoint.Address.GetAddressBytes (), endpoint.Port);
			CheckError (ret);
		}

		public void Accept ()
		{
			var ret = native_openssl_accept (handle);
			CheckError (ret);
		}

		public void SetCertificate (byte[] data)
		{
			native_openssl_load_certificate_from_pem (handle, data, data.Length);
		}

		public void SetPrivateKey (byte[] data)
		{
			native_openssl_load_private_key_from_pem (handle, data, data.Length);
		}

		public void SetCertificate (byte[] data, string password)
		{
			var ret = native_openssl_load_certificate_from_pkcs12 (
				handle, data, data.Length, password, password != null ? password.Length : 0,
				out certificate, out privateKey);
			CheckError (ret);

			ret = native_openssl_set_certificate (handle, certificate, privateKey);
			CheckError (ret);
		}

		static X509Certificate ReadNativeCertificate (IntPtr ptr)
		{
			var bio = BIO_new (BIO_s_mem ());
			try {
				var ret = PEM_write_bio_X509 (bio, ptr);
				if (ret <= 0)
					throw new NativeOpenSslException (NativeOpenSslError.INVALID_CERT);
				IntPtr data;
				var size = native_openssl_BIO_get_mem_data (bio, out data);
				var buffer = new byte [size];
				Marshal.Copy (data, buffer, 0, size);
				return new X509Certificate (buffer);
			} finally {
				BIO_free (bio);
			}
		}

		int OnVerifyCallback (int ok, IntPtr store_ctx)
		{
			try {
				var cert = X509_STORE_CTX_get_current_cert (store_ctx);
				var managedCert = ReadNativeCertificate (cert);
				var ret = managed_cert_callback (ok != 0, managedCert);
				return ret ? 1 : 0;
			} catch (Exception ex) {
				Debug ("EXCEPTION IN VERIFY CALLBACK: {0}", ex);
				return 0;
			}
		}

		int OnCertificateVerifyCallback (IntPtr store_ctx, IntPtr cert)
		{
			try {
				Debug ("CERT VERIFY CALLBACK: {0:x} {1:x}", store_ctx.ToInt32 (), cert.ToInt32 ());
				var managedCert = ReadNativeCertificate (cert);
				Debug ("GOT CERTIFICATE: {0} {1}", managedCert, managedCert.Subject);
			} catch (Exception ex) {
				Debug ("EXCEPTION IN CERT VERIFY CALLBACK: {0}", ex);
			}
			return 0;
		}

		[Flags]
		public enum VerifyMode {
			SSL_VERIFY_NONE= 0x00,
			SSL_VERIFY_PEER = 0x01,
			SSL_VERIFY_FAIL_IF_NO_PEER_CERT = 0x02,
			SSL_VERIFY_CLIENT_ONCE = 0x04
		}

		public void SetCertificateVerify (VerifyMode mode, RemoteValidationCallback callback)
		{
			this.managed_cert_callback = callback;
			if (callback != null)
				verify_callback = new VerifyCallback (OnVerifyCallback);
			native_openssl_set_certificate_verify (handle, (int)mode, verify_callback, null, 10);
		}

		enum ShutdownState {
			None,
			Error,
			Closed,
			SentShutdown
		}

		bool Shutdown_internal (bool waitForReply)
		{
			Debug ("SHUTDOWN: {0} {1}", shutdownState, waitForReply);
			if (shutdownState == ShutdownState.Error)
				return false;
			else if (shutdownState == ShutdownState.Closed)
				return true;

			try {
				var ret = native_openssl_shutdown (handle);
				Debug ("SHUTDOWN #1: {0}", ret);
				if (ret == 1) {
					shutdownState = ShutdownState.Closed;
					return true;
				}
				if (!waitForReply) {
					shutdownState = ShutdownState.SentShutdown;
					return false;
				}
				ret = native_openssl_shutdown (handle);
				Debug ("SHUTDOWN #2: {0}", ret);
				if (ret != 1)
					throw new IOException ("Shutdown failed.");
				shutdownState = ShutdownState.Closed;
				return true;
			} finally {
				lockReadState = 0;
			}
		}

		public IAsyncResult BeginShutdown (bool waitForReply, AsyncCallback callback, object state)
		{
			if (Interlocked.CompareExchange (ref lockReadState, 1, 0) != 0)
				throw GetConcurrentOperationEx ();
			if (Interlocked.CompareExchange (ref lockWriteState, 1, 0) != 0) {
				lockReadState = 0;
				throw GetConcurrentOperationEx ();
			}

			try {
				return shutdownHandler.BeginInvoke (waitForReply, callback, state);
			} catch {
				lockWriteState = 0;
				lockReadState = 0;
				throw;
			}
		}

		public bool EndShutdown (IAsyncResult result)
		{
			try {
				return shutdownHandler.EndInvoke (result);
			} finally {
				lockWriteState = 0;
				lockReadState = 0;
			}
		}

		public CipherSuiteCode CurrentCipher {
			get { return (CipherSuiteCode)native_openssl_get_current_cipher (handle); }
		}

		public void SetCipherList (ICollection<CipherSuiteCode> ciphers)
		{
			var codes = new TlsBuffer (ciphers.Count * 2);
			foreach (var cipher in ciphers)
				codes.Write ((short)cipher);

			var ret = native_openssl_set_cipher_list (handle, codes.Buffer, ciphers.Count);
			CheckError (ret);
		}

		protected override void Dispose (bool disposing)
		{
			if (disposing) {
				if (certificate != null) {
					certificate.Dispose ();
					certificate = null;
				}
				if (privateKey != null) {
					privateKey.Dispose ();
					privateKey = null;
				}
				if (handle != null) {
					native_openssl_close (handle);
					handle.Dispose ();
					handle = null;
				}
			}
			base.Dispose (disposing);
		}
	}
}

