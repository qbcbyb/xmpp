//XMPP .NET Library Copyright (C) 2006, 2008 Dieter Lunn
//
//This library is free software; you can redistribute it and/or modify it under
//the terms of the GNU Lesser General Public License as published by the Free
//Software Foundation; either version 3 of the License, or (at your option)
//any later version.

//This library is distributed in the hope that it will be useful, but WITHOUT
//ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
//FOR A PARTICULAR PURPOSE. See the GNU Lesser General Public License for more
//
//You should have received a copy of the GNU Lesser General Public License along
//with this library; if not, write to the Free Software Foundation, Inc., 59
//Temple Place, Suite 330, Boston, MA 02111-1307 USA

using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using xmpp.logging;

using System.Diagnostics;

namespace xmpp.net
{
    /// <remarks>
    /// AsyncSocket is the class that communicates with the server.
    /// </remarks>
	public class AsyncSocket
	{
        const int ConnectPortNo = 5222;
		// Port 5223 for SSL connections has been deprecated.  SSL is now determined by stream features.
		const int SslConnectPortNo = 5222;

		private Socket _socket;
		private Decoder _decoder = Encoding.UTF8.GetDecoder();
		private UTF8Encoding _utf = new UTF8Encoding();
		private Address _dest;
		private byte[] _buff = new byte[4096];
		private Stream _stream;
		private string _hostname;
		private bool _ssl;
		private bool _secure;
		private NetworkStream _netstream;
        private int _port = 0;

		// Used to determine if we are encrypting the socket to turn off returning the message to the parser
		private bool _encrypting = false;
		private SslStream _sslstream;
		//private ManualResetEvent _resetEvent = new ManualResetEvent(false);

        /// <summary>
        /// Occurs when a connection is established with a server.
        /// </summary>
		public event EventHandler Connection;

        /// <summary>
        /// Occurs when a message has been received from the server.
        /// </summary>
		public event EventHandler<MessageEventArgs> Message;

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncSocket"/> class.
        /// </summary>
		public AsyncSocket()
		{
		}

        /// <summary>
        /// Establishes a connection to the specified remote host.
        /// </summary>
        /// <returns>True if we connected, false if we didn't</returns>
		public bool Connect()
		{
            if (_port == 0)
            {
                if (SSL)
                    _port = SslConnectPortNo;
                else
                    _port = ConnectPortNo;
            }

			_dest = Address.Resolve(_hostname, _port);
			Logger.InfoFormat(this, "Connecting to: {0} on port {1}", _dest.IP.ToString(), _port.ToString());
			_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                _socket.Connect(_dest.EndPoint);
            }
            catch (SocketException)
			{
                //We Failed to connect
                //TODO: Return an error so that the hosting application can take action.
            }
            if (_socket.Connected)
            {
                _netstream = new NetworkStream(_socket, true);
                _stream = _netstream;
                _stream.BeginRead(_buff, 0, _buff.Length, new AsyncCallback(Receive), null);
                OnConnect();
                return true;
            }
            return false;
		}

        /// <summary>
        /// Encrypts the connection using SSL/TLS
        /// </summary>
        public void StartSecure()
        {
			//_encrypting = true;
			Logger.Debug(this, "Starting .NET Secure Mode");
			_sslstream = new SslStream(_stream, true, new RemoteCertificateValidationCallback(RemoteValidation));
			Logger.Debug(this, "Authenticating as Client");
			try
			{
				_sslstream.AuthenticateAsClient(_dest.Hostname, null, SslProtocols.Tls, false);
				if (_sslstream.IsAuthenticated)
				{
					_stream = _sslstream;
					//_resetEvent.Set();
				}
				//_resetEvent.WaitOne();
			} catch (Exception e)
			{
				Logger.ErrorFormat(this, "SSL Error: {0}", e);
			}
			//_encrypting = false;
        }
		
		/*
		private void EndAuthenticate(IAsyncResult result)
		{
			_sslstream.EndAuthenticateAsClient(result);
			if (_sslstream.IsAuthenticated)
			{
				_stream = _sslstream;
				_resetEvent.Set();
			}
		} */

        private static bool RemoteValidation(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors)
        {
            if (errors == SslPolicyErrors.None)
            {
                return true;
            }

			Logger.DebugFormat(typeof(AsyncSocket), "Policy Errors: {0}", errors);
            return false;
        }

        /// <summary>
        /// Closes the current socket.
        /// </summary>
        public void Close()
        {
            Logger.Debug(this, "Closing socket (Graceful Shutdown)");
            _socket.Close();
        }

        /// <summary>
        /// Writes data to the current connection.
        /// </summary>
        /// <param name="msg">Message to send</param>
		public void Write(string msg)
		{
			Logger.DebugFormat(this, "Outgoing Message: {0}", msg);
            byte[] mesg = _utf.GetBytes(msg);
			//_socket.Send(mesg, 0, mesg.Length, SocketFlags.None);
			_stream.Write(mesg, 0, mesg.Length);
		}

		private void OnConnect()
		{
			if (Connection != null)
			{
				Connection(this, EventArgs.Empty);
			}
		}

		private void OnMessage(String message)
		{
			if (Message != null)
			{
				Message(this, new MessageEventArgs(message));
			}
		}

		private void Receive(IAsyncResult ar)
		{
			try
			{
				int rx = _stream.EndRead(ar);
				char[] chars = new char[rx];
				_decoder.GetChars(_buff, 0, rx, chars, 0);
				string msg = new string(chars);
				Logger.DebugFormat(this, "Incoming Message: {0}", msg);
				_stream.BeginRead(_buff, 0, _buff.Length, new AsyncCallback(Receive), null);
				if (!_encrypting)
					OnMessage(msg);
			}
			catch (Exception e)
			{
				Logger.ErrorFormat(this, "General Exception in socket receive: {0}", e);
			}
/*			catch (SocketException e)
			{
				Logger.DebugFormat(this, "Socket Exception: {0}", e);
			}
			catch (InvalidOperationException e)
			{
				Logger.DebugFormat(this, "Invalid Operation: {0}", e);
			}
			*/
		}

        /// <summary>
        /// Gets the current status of the socket.
        /// </summary>
		public bool Connected
		{
			get { return _socket.Connected; }
		}
		
		/// <value>
		/// 
		/// </value>
		public string Hostname
		{
			get { return _hostname; }
			set { _hostname = value; }
		}
		
		/// <value>
		/// 
		/// </value>
		public bool SSL
		{
			get { return _ssl; }
			set { _ssl = value; }
		}
		
		/// <value>
		/// 
		/// </value>
		public bool Secure
		{
			get { return _secure; }
			set { _secure = value; }
		}

        public int Port
        {
            get { return _port; }
            set { _port = value; }
        }
	}

	/// <remarks>
	/// Provides data for the Message events.
	/// </remarks>
	public class MessageEventArgs : EventArgs
	{
		private string _message;

		/// <summary>
		/// Initializes a new instance of the <see cref="MessageEventArgs"/> class.
		/// </summary>
		/// <param name="message">The message received from the stream</param>
		public MessageEventArgs(String message)
		{
			_message = message;
		}

		/// <summary>
		/// Gets the message received from the stream.
		/// </summary>
		public String Message
		{
			get { return _message; }
			set { _message = value; }
		}
	}
}
