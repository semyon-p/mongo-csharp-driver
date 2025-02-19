/* Copyright 2010-present MongoDB Inc.
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
* http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver.Core.Bindings;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Clusters.ServerSelectors;
using MongoDB.Driver.Core.Misc;
using MongoDB.Driver.Core.Operations;
using MongoDB.Driver.Core.Servers;
using MongoDB.Driver.Core.WireProtocol.Messages.Encoders;
using MongoDB.Driver.Encryption;
using MongoDB.Driver.Linq;

namespace MongoDB.Driver
{
    /// <inheritdoc/>
    public class MongoClient : MongoClientBase, IDisposable
    {
        #region static
        // private static methods
        private static IEnumerable<ServerDescription> SelectServersThatDetermineWhetherSessionsAreSupported(ClusterDescription cluster, IEnumerable<ServerDescription> servers)
        {
            var connectedServers = servers.Where(s => s.State == ServerState.Connected);

            if (cluster.IsDirectConnection)
            {
                return connectedServers;
            }
            else
            {
                return connectedServers.Where(s => s.IsDataBearing);
            }
        }
        #endregion

        // private fields
        private readonly ICluster _cluster;
        private readonly AutoEncryptionLibMongoCryptController _libMongoCryptController;
        private readonly LinqProvider _linqProvider;
        private readonly IOperationExecutor _operationExecutor;
        private readonly MongoClientSettings _settings;

        // constructors
        /// <summary>
        /// Initializes a new instance of the MongoClient class.
        /// </summary>
        public MongoClient()
            : this(new MongoClientSettings())
        {
        }

        /// <summary>
        /// Initializes a new instance of the MongoClient class.
        /// </summary>
        /// <param name="settings">The settings.</param>
        public MongoClient(MongoClientSettings settings)
        {
            _settings = Ensure.IsNotNull(settings, nameof(settings)).FrozenCopy();
            _linqProvider = _settings.LinqProvider;
            _cluster = ClusterRegistry.Instance.GetOrCreateCluster(_settings.ToClusterKey());
            _operationExecutor = new OperationExecutor(this);
            if (settings.AutoEncryptionOptions != null)
            {
                _libMongoCryptController = AutoEncryptionLibMongoCryptController.Create(
                    this,
                    _cluster.CryptClient,
                    settings.AutoEncryptionOptions);
            }
        }

        /// <summary>
        /// Initializes a new instance of the MongoClient class.
        /// </summary>
        /// <param name="url">The URL.</param>
        public MongoClient(MongoUrl url)
            : this(MongoClientSettings.FromUrl(url))
        {
        }

        /// <summary>
        /// Initializes a new instance of the MongoClient class.
        /// </summary>
        /// <param name="connectionString">The connection string.</param>
        public MongoClient(string connectionString)
            : this(MongoClientSettings.FromConnectionString(connectionString))
        {
        }

        internal MongoClient(IOperationExecutor operationExecutor, MongoClientSettings settings)
            : this(settings)
        {
            _operationExecutor = operationExecutor;
        }

        // public properties
        /// <summary>
        /// Gets the cluster.
        /// </summary>
        public override ICluster Cluster
        {
            get { return _cluster; }
        }

        /// <inheritdoc/>
        public sealed override MongoClientSettings Settings
        {
            get { return _settings; }
        }

        // internal properties
        internal AutoEncryptionLibMongoCryptController LibMongoCryptController => _libMongoCryptController;
        internal IOperationExecutor OperationExecutor => _operationExecutor;

        // internal methods
        internal void ConfigureAutoEncryptionMessageEncoderSettings(MessageEncoderSettings messageEncoderSettings)
        {
            var autoEncryptionOptions = _settings.AutoEncryptionOptions;
            if (autoEncryptionOptions != null)
            {
                if (!autoEncryptionOptions.BypassAutoEncryption)
                {
                    messageEncoderSettings.Add(MessageEncoderSettingsName.BinaryDocumentFieldEncryptor, _libMongoCryptController);
                }
                messageEncoderSettings.Add(MessageEncoderSettingsName.BinaryDocumentFieldDecryptor, _libMongoCryptController);
            }
        }

        // private static methods


        // public methods
        /// <inheritdoc/>
        public sealed override void DropDatabase(string name, CancellationToken cancellationToken = default(CancellationToken))
        {
            UsingImplicitSession(session => DropDatabase(session, name, cancellationToken), cancellationToken);
        }

        /// <inheritdoc/>
        public sealed override void DropDatabase(IClientSessionHandle session, string name, CancellationToken cancellationToken = default(CancellationToken))
        {
            Ensure.IsNotNull(session, nameof(session));
            var messageEncoderSettings = GetMessageEncoderSettings();
            var operation = new DropDatabaseOperation(new DatabaseNamespace(name), messageEncoderSettings)
            {
                WriteConcern = _settings.WriteConcern
            };
            ExecuteWriteOperation(session, operation, cancellationToken);
        }

        /// <inheritdoc/>
        public sealed override Task DropDatabaseAsync(string name, CancellationToken cancellationToken = default(CancellationToken))
        {
            return UsingImplicitSessionAsync(session => DropDatabaseAsync(session, name, cancellationToken), cancellationToken);
        }

        /// <inheritdoc/>
        public sealed override Task DropDatabaseAsync(IClientSessionHandle session, string name, CancellationToken cancellationToken = default(CancellationToken))
        {
            Ensure.IsNotNull(session, nameof(session));
            var messageEncoderSettings = GetMessageEncoderSettings();
            var operation = new DropDatabaseOperation(new DatabaseNamespace(name), messageEncoderSettings)
            {
                WriteConcern = _settings.WriteConcern
            };
            return ExecuteWriteOperationAsync(session, operation, cancellationToken);
        }

        /// <inheritdoc/>
        public sealed override IMongoDatabase GetDatabase(string name, MongoDatabaseSettings settings = null)
        {
            settings = settings == null ?
                new MongoDatabaseSettings() :
                settings.Clone();

            settings.ApplyDefaultValues(_settings);

            return new MongoDatabaseImpl(this, new DatabaseNamespace(name), settings, _cluster, _operationExecutor);
        }

        /// <inheritdoc />
        public sealed override IAsyncCursor<string> ListDatabaseNames(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ListDatabaseNames(options: null, cancellationToken);
        }

        /// <inheritdoc />
        public sealed override IAsyncCursor<string> ListDatabaseNames(
            ListDatabaseNamesOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return UsingImplicitSession(session => ListDatabaseNames(session, options, cancellationToken), cancellationToken);
        }

        /// <inheritdoc />
        public sealed override IAsyncCursor<string> ListDatabaseNames(
            IClientSessionHandle session,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ListDatabaseNames(session, options: null, cancellationToken);
        }

        /// <inheritdoc />
        public sealed override IAsyncCursor<string> ListDatabaseNames(
            IClientSessionHandle session,
            ListDatabaseNamesOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var listDatabasesOptions = CreateListDatabasesOptionsFromListDatabaseNamesOptions(options);
            var databases = ListDatabases(session, listDatabasesOptions, cancellationToken);

            return CreateDatabaseNamesCursor(databases);
        }

        /// <inheritdoc />
        public sealed override Task<IAsyncCursor<string>> ListDatabaseNamesAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ListDatabaseNamesAsync(options: null, cancellationToken);
        }

        /// <inheritdoc />
        public sealed override Task<IAsyncCursor<string>> ListDatabaseNamesAsync(
            ListDatabaseNamesOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return UsingImplicitSessionAsync(session => ListDatabaseNamesAsync(session, options, cancellationToken), cancellationToken);
        }

        /// <inheritdoc />
        public sealed override Task<IAsyncCursor<string>> ListDatabaseNamesAsync(
            IClientSessionHandle session,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ListDatabaseNamesAsync(session, options: null, cancellationToken);
        }

        /// <inheritdoc />
        public sealed override async Task<IAsyncCursor<string>> ListDatabaseNamesAsync(
            IClientSessionHandle session,
            ListDatabaseNamesOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var listDatabasesOptions = CreateListDatabasesOptionsFromListDatabaseNamesOptions(options);
            var databases = await ListDatabasesAsync(session, listDatabasesOptions, cancellationToken).ConfigureAwait(false);

            return CreateDatabaseNamesCursor(databases);
        }

        /// <inheritdoc/>
        public sealed override IAsyncCursor<BsonDocument> ListDatabases(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return UsingImplicitSession(session => ListDatabases(session, cancellationToken), cancellationToken);
        }

        /// <inheritdoc/>
        public sealed override IAsyncCursor<BsonDocument> ListDatabases(
            ListDatabasesOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return UsingImplicitSession(session => ListDatabases(session, options, cancellationToken), cancellationToken);
        }

        /// <inheritdoc/>
        public sealed override IAsyncCursor<BsonDocument> ListDatabases(
            IClientSessionHandle session,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ListDatabases(session, null, cancellationToken);
        }

        /// <inheritdoc/>
        public sealed override IAsyncCursor<BsonDocument> ListDatabases(
            IClientSessionHandle session,
            ListDatabasesOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Ensure.IsNotNull(session, nameof(session));
            options = options ?? new ListDatabasesOptions();
            var messageEncoderSettings = GetMessageEncoderSettings();
            var operation = CreateListDatabaseOperation(options, messageEncoderSettings);
            return ExecuteReadOperation(session, operation, cancellationToken);
        }

        /// <inheritdoc/>
        public sealed override Task<IAsyncCursor<BsonDocument>> ListDatabasesAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return UsingImplicitSessionAsync(session => ListDatabasesAsync(session, null, cancellationToken), cancellationToken);
        }

        /// <inheritdoc/>
        public sealed override Task<IAsyncCursor<BsonDocument>> ListDatabasesAsync(
            ListDatabasesOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return UsingImplicitSessionAsync(session => ListDatabasesAsync(session, options, cancellationToken), cancellationToken);
        }

        /// <inheritdoc/>
        public sealed override Task<IAsyncCursor<BsonDocument>> ListDatabasesAsync(
            IClientSessionHandle session,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ListDatabasesAsync(session, null, cancellationToken);
        }

        /// <inheritdoc/>
        public sealed override Task<IAsyncCursor<BsonDocument>> ListDatabasesAsync(
            IClientSessionHandle session,
            ListDatabasesOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Ensure.IsNotNull(session, nameof(session));
            options = options ?? new ListDatabasesOptions();
            var messageEncoderSettings = GetMessageEncoderSettings();
            var operation = CreateListDatabaseOperation(options, messageEncoderSettings);
            return ExecuteReadOperationAsync(session, operation, cancellationToken);
        }

        /// <summary>
        /// Starts an implicit session.
        /// </summary>
        /// <returns>A session.</returns>
        internal IClientSessionHandle StartImplicitSession(CancellationToken cancellationToken)
        {
            var areSessionsSupported = AreSessionsSupported(cancellationToken);
            return StartImplicitSession(areSessionsSupported);
        }

        /// <summary>
        /// Starts an implicit session.
        /// </summary>
        /// <returns>A Task whose result is a session.</returns>
        internal async Task<IClientSessionHandle> StartImplicitSessionAsync(CancellationToken cancellationToken)
        {
            var areSessionsSupported = await AreSessionsSupportedAsync(cancellationToken).ConfigureAwait(false);
            return StartImplicitSession(areSessionsSupported);
        }

        /// <inheritdoc/>
        public sealed override IClientSessionHandle StartSession(ClientSessionOptions options = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var areSessionsSupported = AreSessionsSupported(cancellationToken);
            return StartSession(options, areSessionsSupported);
        }

        /// <inheritdoc/>
        public sealed override async Task<IClientSessionHandle> StartSessionAsync(ClientSessionOptions options = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var areSessionsSupported = await AreSessionsSupportedAsync(cancellationToken).ConfigureAwait(false);
            return StartSession(options, areSessionsSupported);
        }

        /// <inheritdoc/>
        public override IChangeStreamCursor<TResult> Watch<TResult>(
            PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
            ChangeStreamOptions options = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return UsingImplicitSession(session => Watch(session, pipeline, options, cancellationToken), cancellationToken);
        }

        /// <inheritdoc/>
        public override IChangeStreamCursor<TResult> Watch<TResult>(
            IClientSessionHandle session,
            PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
            ChangeStreamOptions options = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Ensure.IsNotNull(session, nameof(session));
            Ensure.IsNotNull(pipeline, nameof(pipeline));
            var operation = CreateChangeStreamOperation(pipeline, options);
            return ExecuteReadOperation(session, operation, cancellationToken);
        }

        /// <inheritdoc/>
        public override Task<IChangeStreamCursor<TResult>> WatchAsync<TResult>(
            PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
            ChangeStreamOptions options = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return UsingImplicitSessionAsync(session => WatchAsync(session, pipeline, options, cancellationToken), cancellationToken);
        }

        /// <inheritdoc/>
        public override Task<IChangeStreamCursor<TResult>> WatchAsync<TResult>(
            IClientSessionHandle session,
            PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
            ChangeStreamOptions options = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Ensure.IsNotNull(session, nameof(session));
            Ensure.IsNotNull(pipeline, nameof(pipeline));
            var operation = CreateChangeStreamOperation(pipeline, options);
            return ExecuteReadOperationAsync(session, operation, cancellationToken);
        }

        /// <inheritdoc/>
        public override IMongoClient WithReadConcern(ReadConcern readConcern)
        {
            Ensure.IsNotNull(readConcern, nameof(readConcern));
            var newSettings = Settings.Clone();
            newSettings.ReadConcern = readConcern;
            return new MongoClient(_operationExecutor, newSettings);
        }

        /// <inheritdoc/>
        public override IMongoClient WithReadPreference(ReadPreference readPreference)
        {
            Ensure.IsNotNull(readPreference, nameof(readPreference));
            var newSettings = Settings.Clone();
            newSettings.ReadPreference = readPreference;
            return new MongoClient(_operationExecutor, newSettings);
        }

        /// <inheritdoc/>
        public override IMongoClient WithWriteConcern(WriteConcern writeConcern)
        {
            Ensure.IsNotNull(writeConcern, nameof(writeConcern));
            var newSettings = Settings.Clone();
            newSettings.WriteConcern = writeConcern;
            return new MongoClient(_operationExecutor, newSettings);
        }

        // private methods
        private bool AreSessionsSupported(CancellationToken cancellationToken)
        {
            return AreSessionsSupported(_cluster.Description) ?? AreSessionsSupportedAfterServerSelection(cancellationToken);
        }

        private async Task<bool> AreSessionsSupportedAsync(CancellationToken cancellationToken)
        {
            return AreSessionsSupported(_cluster.Description) ?? await AreSessionsSupportedAfterServerSelectionAsync(cancellationToken).ConfigureAwait(false);
        }

        private bool? AreSessionsSupported(ClusterDescription clusterDescription)
        {
            if (clusterDescription.LogicalSessionTimeout.HasValue || clusterDescription.Type == ClusterType.LoadBalanced)
            {
                return true;
            }
            else
            {
                var selectedServers = SelectServersThatDetermineWhetherSessionsAreSupported(clusterDescription, clusterDescription.Servers).ToList();
                if (selectedServers.Count == 0)
                {
                    return null;
                }
                else
                {
                    return false;
                }
            }
        }

        private bool AreSessionsSupportedAfterServerSelection(CancellationToken cancellationToken)
        {
            var selector = new AreSessionsSupportedServerSelector();
            var selectedServer = _cluster.SelectServer(selector, cancellationToken);
            var clusterDescription = selector.ClusterDescription ?? _cluster.Description; // LB cluster doesn't use server selector, so clusterDescription is null for this case
            return AreSessionsSupported(clusterDescription) ?? false;
        }

        private async Task<bool> AreSessionsSupportedAfterServerSelectionAsync(CancellationToken cancellationToken)
        {
            var selector = new AreSessionsSupportedServerSelector();
            var selectedServer = await _cluster.SelectServerAsync(selector, cancellationToken).ConfigureAwait(false);
            var clusterDescription = selector.ClusterDescription ?? _cluster.Description;  // LB cluster doesn't use server selector, so clusterDescription is null for this case
            return AreSessionsSupported(clusterDescription) ?? false;
        }

        private IAsyncCursor<string> CreateDatabaseNamesCursor(IAsyncCursor<BsonDocument> cursor)
        {
            return new BatchTransformingAsyncCursor<BsonDocument, string>(
                cursor,
                databases => databases.Select(database => database["name"].AsString));
        }

        private ListDatabasesOperation CreateListDatabaseOperation(
            ListDatabasesOptions options,
            MessageEncoderSettings messageEncoderSettings)
        {
            return new ListDatabasesOperation(messageEncoderSettings)
            {
                AuthorizedDatabases = options.AuthorizedDatabases,
                Comment = options.Comment,
                Filter = options.Filter?.Render(BsonDocumentSerializer.Instance, BsonSerializer.SerializerRegistry, _linqProvider),
                NameOnly = options.NameOnly,
                RetryRequested = _settings.RetryReads
            };
        }

        private ListDatabasesOptions CreateListDatabasesOptionsFromListDatabaseNamesOptions(ListDatabaseNamesOptions options)
        {
            var listDatabasesOptions = new ListDatabasesOptions { NameOnly = true };
            if (options != null)
            {
                listDatabasesOptions.AuthorizedDatabases = options.AuthorizedDatabases;
                listDatabasesOptions.Filter = options.Filter;
                listDatabasesOptions.Comment = options.Comment;
            }

            return listDatabasesOptions;
        }

        private IReadBindingHandle CreateReadBinding(IClientSessionHandle session)
        {
            var readPreference = _settings.ReadPreference;
            if (session.IsInTransaction && readPreference.ReadPreferenceMode != ReadPreferenceMode.Primary)
            {
                throw new InvalidOperationException("Read preference in a transaction must be primary.");
            }

            var binding = new ReadPreferenceBinding(_cluster, readPreference, session.WrappedCoreSession.Fork());
            return new ReadBindingHandle(binding);
        }

        private IReadWriteBindingHandle CreateReadWriteBinding(IClientSessionHandle session)
        {
            var binding = new WritableServerBinding(_cluster, session.WrappedCoreSession.Fork());
            return new ReadWriteBindingHandle(binding);
        }

        private ChangeStreamOperation<TResult> CreateChangeStreamOperation<TResult>(
            PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
            ChangeStreamOptions options)
        {
            return ChangeStreamHelper.CreateChangeStreamOperation(
                pipeline,
                _linqProvider,
                options,
                _settings.ReadConcern,
                GetMessageEncoderSettings(),
                _settings.RetryReads);
        }

        private TResult ExecuteReadOperation<TResult>(IClientSessionHandle session, IReadOperation<TResult> operation, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var binding = CreateReadBinding(session))
            {
                return _operationExecutor.ExecuteReadOperation(binding, operation, cancellationToken);
            }
        }

        private async Task<TResult> ExecuteReadOperationAsync<TResult>(IClientSessionHandle session, IReadOperation<TResult> operation, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var binding = CreateReadBinding(session))
            {
                return await _operationExecutor.ExecuteReadOperationAsync(binding, operation, cancellationToken).ConfigureAwait(false);
            }
        }

        private TResult ExecuteWriteOperation<TResult>(IClientSessionHandle session, IWriteOperation<TResult> operation, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var binding = CreateReadWriteBinding(session))
            {
                return _operationExecutor.ExecuteWriteOperation(binding, operation, cancellationToken);
            }
        }

        private async Task<TResult> ExecuteWriteOperationAsync<TResult>(IClientSessionHandle session, IWriteOperation<TResult> operation, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var binding = CreateReadWriteBinding(session))
            {
                return await _operationExecutor.ExecuteWriteOperationAsync(binding, operation, cancellationToken).ConfigureAwait(false);
            }
        }

        private MessageEncoderSettings GetMessageEncoderSettings()
        {
            var messageEncoderSettings = new MessageEncoderSettings
            {
                { MessageEncoderSettingsName.ReadEncoding, _settings.ReadEncoding ?? Utf8Encodings.Strict },
                { MessageEncoderSettingsName.WriteEncoding, _settings.WriteEncoding ?? Utf8Encodings.Strict }
            };
#pragma warning disable 618
            if (BsonDefaults.GuidRepresentationMode == GuidRepresentationMode.V2)
            {
                messageEncoderSettings.Add(MessageEncoderSettingsName.GuidRepresentation, _settings.GuidRepresentation);
            }
#pragma warning restore 618

            ConfigureAutoEncryptionMessageEncoderSettings(messageEncoderSettings);

            return messageEncoderSettings;
        }

        private IClientSessionHandle StartImplicitSession(bool areSessionsSupported)
        {
            var options = new ClientSessionOptions { CausalConsistency = false, Snapshot = false };

            ICoreSessionHandle coreSession;
#pragma warning disable 618
            var areMultipleUsersAuthenticated = _settings.Credentials.Count() > 1;
#pragma warning restore
            if (areSessionsSupported && !areMultipleUsersAuthenticated)
            {
                coreSession = _cluster.StartSession(options.ToCore(isImplicit: true));
            }
            else
            {
                coreSession = NoCoreSession.NewHandle();
            }

            return new ClientSessionHandle(this, options, coreSession);
        }

        private IClientSessionHandle StartSession(ClientSessionOptions options, bool areSessionsSupported)
        {
            if (!areSessionsSupported)
            {
                throw new NotSupportedException("Sessions are not supported by this version of the server.");
            }

            if (options != null && options.Snapshot && options.CausalConsistency == true)
            {
                throw new NotSupportedException("Combining both causal consistency and snapshot options is not supported.");
            }

            options = options ?? new ClientSessionOptions();
            var coreSession = _cluster.StartSession(options.ToCore());

            return new ClientSessionHandle(this, options, coreSession);
        }

        private void UsingImplicitSession(Action<IClientSessionHandle> func, CancellationToken cancellationToken)
        {
            using (var session = StartImplicitSession(cancellationToken))
            {
                func(session);
            }
        }

        private TResult UsingImplicitSession<TResult>(Func<IClientSessionHandle, TResult> func, CancellationToken cancellationToken)
        {
            using (var session = StartImplicitSession(cancellationToken))
            {
                return func(session);
            }
        }

        private async Task UsingImplicitSessionAsync(Func<IClientSessionHandle, Task> funcAsync, CancellationToken cancellationToken)
        {
            using (var session = await StartImplicitSessionAsync(cancellationToken).ConfigureAwait(false))
            {
                await funcAsync(session).ConfigureAwait(false);
            }
        }

        private async Task<TResult> UsingImplicitSessionAsync<TResult>(Func<IClientSessionHandle, Task<TResult>> funcAsync, CancellationToken cancellationToken)
        {
            using (var session = await StartImplicitSessionAsync(cancellationToken).ConfigureAwait(false))
            {
                return await funcAsync(session).ConfigureAwait(false);
            }
        }

        // nested types
        private class AreSessionsSupportedServerSelector : IServerSelector
        {
            public ClusterDescription ClusterDescription;

            public IEnumerable<ServerDescription> SelectServers(ClusterDescription cluster, IEnumerable<ServerDescription> servers)
            {
                ClusterDescription = cluster;
                return SelectServersThatDetermineWhetherSessionsAreSupported(cluster, servers);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_cluster != null)
                _cluster.Dispose();
        }
    }
}
