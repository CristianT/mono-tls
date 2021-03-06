﻿using System;
using Mono.Security.Interface;

namespace Mono.Security.NewTls.Handshake
{
	internal abstract class Message
	{
		public HandshakeType Type {
			get;
			private set;
		}

		public ContentType ContentType {
			get;
			private set;
		}

		public Message (ContentType contentType, HandshakeType type)
		{
			ContentType = contentType;
			Type = type;
		}

		protected abstract void Read (TlsBuffer incoming);
	}
}

