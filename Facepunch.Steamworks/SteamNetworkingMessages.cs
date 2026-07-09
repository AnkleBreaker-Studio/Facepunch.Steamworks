using Steamworks.Data;
using System;
using System.Runtime.InteropServices;

namespace Steamworks
{
	[Flags]
	public enum SteamNetworkingOptions
	{
		Unreliable = 0,
		NoNagle = 1,
		UnreliableNoNagle = Unreliable | NoNagle,
		NoDelay = 4,
		UnreliableNoDelay = Unreliable | NoDelay | NoNagle,
		Reliable = 8,
		ReliableNoNagle = Reliable | NoNagle,
		AutoRestartBrokenSession = 32
	}

	public class SteamNetworkingMessages : SteamSharedClass<SteamNetworkingMessages>
	{
		internal static ISteamNetworkingMessages Internal => Interface as ISteamNetworkingMessages;

		internal override bool InitializeInterface( bool server )
		{
			SetInterface( server, new ISteamNetworkingMessages( server ) );
			if ( Interface.Self == IntPtr.Zero ) return false;

			InstallEvents( server );

			return true;
		}

		internal static void InstallEvents( bool server )
		{
			Dispatch.Install<SteamNetworkingMessagesSessionRequest_t>( x => OnSessionRequest?.Invoke( x.IdentityRemote), server );
			Dispatch.Install<SteamNetworkingMessagesSessionFailed_t>( x => OnSessionFailed?.Invoke( x.Info), server );
		}

		public static Action<NetIdentity> OnSessionRequest;

		public static Action<ConnectionInfo> OnSessionFailed;

		public static bool AcceptSessionWithUser( ref NetIdentity identity ) => Internal.AcceptSessionWithUser( ref identity );
		public static bool CloseSessionWithUser( ref NetIdentity identity ) => Internal.CloseSessionWithUser( ref identity );
		public static bool CloseChannelWithUser( ref NetIdentity identity, int channel ) => Internal.CloseChannelWithUser( ref identity, channel );
		public static ConnectionState GetSessionConnectionInfo( ref NetIdentity identity, ref ConnectionInfo info, ref ConnectionStatus status ) => Internal.GetSessionConnectionInfo( ref identity, ref info, ref status );

		public static unsafe Result SendMessageToUser( ref NetIdentity identity, byte[] data, SteamNetworkingOptions flags, int channel)
		{
			uint length = (uint)data.Length;
			fixed ( byte* p = data )
			{
				return Internal.SendMessageToUser( ref identity, (IntPtr)p, length, (int)flags, channel );
			}
		}

		/// <summary>
		/// Raw send — the caller owns the buffer. Zero managed allocation;
		/// pairs with pooled/pinned buffers or native memory.
		/// </summary>
		public static Result SendMessageToUser( ref NetIdentity identity, IntPtr data, uint length, SteamNetworkingOptions flags, int channel )
		{
			return Internal.SendMessageToUser( ref identity, data, length, (int)flags, channel );
		}

#if NETSTANDARD2_1_OR_GREATER || NET
		/// <summary>
		/// Span send — zero managed allocation (slices of pooled arrays,
		/// stackalloc, etc.). Available on netstandard2.1+/.NET builds.
		/// </summary>
		public static unsafe Result SendMessageToUser( ref NetIdentity identity, ReadOnlySpan<byte> data, SteamNetworkingOptions flags, int channel )
		{
			fixed ( byte* p = data )
			{
				return Internal.SendMessageToUser( ref identity, (IntPtr)p, (uint)data.Length, (int)flags, channel );
			}
		}
#endif

		/// <summary>
		/// Zero-copy message delivery: the buffer belongs to Steam and is only
		/// valid for the duration of the callback — copy it if you keep it.
		/// Mirrors the sockets-layer <c>OnMessage( IntPtr, int, … )</c> contract.
		/// </summary>
		public delegate void MessageIntercept( NetIdentity identity, int channel, IntPtr data, int size );

		public unsafe static int ReceiveMessagesOnChannel( int channel, Action<SteamId, int, byte[]> callback, int bufferSize = 32, bool receiveToEnd = true )
		{
			if ( bufferSize < 1 || bufferSize > 256 ) throw new ArgumentOutOfRangeException( nameof( bufferSize ) );

			int totalProcessed = 0;
			NetMsg** messageBuffer = stackalloc NetMsg*[bufferSize];

			while ( true )
			{
				int processed = Internal.ReceiveMessagesOnChannel(channel, new IntPtr( &messageBuffer[0] ), bufferSize );
				totalProcessed += processed;

				try
				{
					for ( int i = 0; i < processed; i++ )
					{
						ReceiveMessage( ref messageBuffer[i], callback );
					}
				}
				catch
				{
					for ( int i = 0; i < processed; i++ )
					{
						if ( messageBuffer[i] != null )
						{
							NetMsg.InternalRelease( messageBuffer[i] );
						}
					}

					throw;
				}


				//
				// Keep going if receiveToEnd and we filled the buffer
				//
				if ( !receiveToEnd || processed < bufferSize )
					break;
			}

			return totalProcessed;
		}

		internal unsafe static void ReceiveMessage( ref NetMsg* msg, Action<SteamId, int, byte[]> callback )
		{
			try
			{
				byte[] data = new byte[msg->DataSize];
				Marshal.Copy( msg->DataPtr, data, 0, msg->DataSize );
				callback(msg->Identity.SteamId, msg->Channel, data);
			}
			finally
			{
				//
				// Releases the message
				//
				NetMsg.InternalRelease( msg );
				msg = null;
			}
		}

		/// <summary>
		/// ZERO-ALLOCATION receive: drains up to <paramref name="bufferSize"/>
		/// messages per native call and hands each to
		/// <paramref name="onMessage"/> as a raw pointer + size. The buffer is
		/// Steam-owned and freed when the callback returns — copy it (into a
		/// pooled buffer) if it must outlive the call. Unlike the
		/// <c>byte[]</c> overload, this allocates NOTHING per message, which
		/// is what a per-frame network pump wants.
		/// </summary>
		public unsafe static int ReceiveMessagesOnChannel( int channel, MessageIntercept onMessage, int bufferSize = 32, bool receiveToEnd = true )
		{
			if ( bufferSize < 1 || bufferSize > 256 ) throw new ArgumentOutOfRangeException( nameof( bufferSize ) );

			int totalProcessed = 0;
			NetMsg** messageBuffer = stackalloc NetMsg*[bufferSize];

			while ( true )
			{
				int processed = Internal.ReceiveMessagesOnChannel( channel, new IntPtr( &messageBuffer[0] ), bufferSize );
				totalProcessed += processed;

				try
				{
					for ( int i = 0; i < processed; i++ )
					{
						ReceiveMessageIntercept( ref messageBuffer[i], onMessage );
					}
				}
				catch
				{
					for ( int i = 0; i < processed; i++ )
					{
						if ( messageBuffer[i] != null )
						{
							NetMsg.InternalRelease( messageBuffer[i] );
						}
					}

					throw;
				}

				if ( !receiveToEnd || processed < bufferSize )
					break;
			}

			return totalProcessed;
		}

		internal unsafe static void ReceiveMessageIntercept( ref NetMsg* msg, MessageIntercept onMessage )
		{
			try
			{
				onMessage( msg->Identity, msg->Channel, msg->DataPtr, msg->DataSize );
			}
			finally
			{
				NetMsg.InternalRelease( msg );
				msg = null;
			}
		}
	}
}
