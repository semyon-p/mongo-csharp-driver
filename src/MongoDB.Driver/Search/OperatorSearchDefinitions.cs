﻿/* Copyright 2010-present MongoDB Inc.
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
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver.Core.Misc;
using MongoDB.Driver.GeoJsonObjectModel;

namespace MongoDB.Driver.Search
{
    internal sealed class AutocompleteSearchDefinition<TDocument> : OperatorSearchDefinition<TDocument>
    {
        private readonly SearchFuzzyOptions _fuzzy;
        private readonly SearchQueryDefinition _query;
        private readonly SearchAutocompleteTokenOrder _tokenOrder;

        public AutocompleteSearchDefinition(
            SearchPathDefinition<TDocument> path,
            SearchQueryDefinition query,
            SearchAutocompleteTokenOrder tokenOrder,
            SearchFuzzyOptions fuzzy,
            SearchScoreDefinition<TDocument> score)
                : base(OperatorType.Autocomplete, path, score)
        {
            _query = Ensure.IsNotNull(query, nameof(query));
            _tokenOrder = tokenOrder;
            _fuzzy = fuzzy;
        }

        private protected override BsonDocument RenderArguments(IBsonSerializer<TDocument> documentSerializer, IBsonSerializerRegistry serializerRegistry) =>
           new()
           {
                { "query", _query.Render() },
                { "tokenOrder", _tokenOrder.ToCamelCase(), _tokenOrder != SearchAutocompleteTokenOrder.Any },
                { "fuzzy", () => _fuzzy.Render(), _fuzzy != null },
           };
    }

    internal sealed class CompoundSearchDefinition<TDocument> : OperatorSearchDefinition<TDocument>
    {
        private readonly List<SearchDefinition<TDocument>> _filter;
        private readonly int _minimumShouldMatch;
        private readonly List<SearchDefinition<TDocument>> _must;
        private readonly List<SearchDefinition<TDocument>> _mustNot;
        private readonly List<SearchDefinition<TDocument>> _should;

        public CompoundSearchDefinition(
            List<SearchDefinition<TDocument>> must,
            List<SearchDefinition<TDocument>> mustNot,
            List<SearchDefinition<TDocument>> should,
            List<SearchDefinition<TDocument>> filter,
            int minimumShouldMatch)
                : base(OperatorType.Compound)
        {
            // This constructor should always be called from the compound search definition builder that ensures the arguments are valid.
            _must = must;
            _mustNot = mustNot;
            _should = should;
            _filter = filter;
            _minimumShouldMatch = minimumShouldMatch;
        }

        private protected override BsonDocument RenderArguments(IBsonSerializer<TDocument> documentSerializer, IBsonSerializerRegistry serializerRegistry)
        {
            return new()
            {
                { "must", Render(_must), _must != null },
                { "mustNot", Render(_mustNot), _mustNot != null },
                { "should", Render(_should), _should != null },
                { "filter", Render(_filter), _filter != null },
                { "minimumShouldMatch", _minimumShouldMatch, _minimumShouldMatch > 0 },
            };

            Func<BsonArray> Render(List<SearchDefinition<TDocument>> searchDefinitions) =>
               () => new BsonArray(searchDefinitions.Select(clause => clause.Render(documentSerializer, serializerRegistry)));
        }
    }

    internal sealed class EqualsSearchDefinition<TDocument> : OperatorSearchDefinition<TDocument>
    {
        private readonly BsonValue _value;

        public EqualsSearchDefinition(FieldDefinition<TDocument> path, BsonValue value, SearchScoreDefinition<TDocument> score)
            : base(OperatorType.Equals, path, score)
        {
            _value = value;
        }

        private protected override BsonDocument RenderArguments(IBsonSerializer<TDocument> documentSerializer, IBsonSerializerRegistry serializerRegistry) =>
            new("value", _value);
    }

    internal sealed class ExistsSearchDefinition<TDocument> : OperatorSearchDefinition<TDocument>
    {
        public ExistsSearchDefinition(FieldDefinition<TDocument> path)
            : base(OperatorType.Exists, path, null)
        {
        }
    }

    internal sealed class FacetSearchDefinition<TDocument> : OperatorSearchDefinition<TDocument>
    {
        private readonly SearchFacet<TDocument>[] _facets;
        private readonly SearchDefinition<TDocument> _operator;

        public FacetSearchDefinition(SearchDefinition<TDocument> @operator, IEnumerable<SearchFacet<TDocument>> facets)
            : base(OperatorType.Facet)
        {
            _operator = Ensure.IsNotNull(@operator, nameof(@operator));
            _facets = Ensure.IsNotNull(facets, nameof(facets)).ToArray();
        }

        private protected override BsonDocument RenderArguments(IBsonSerializer<TDocument> documentSerializer, IBsonSerializerRegistry serializerRegistry) =>
            new()
            {
                { "operator", _operator.Render(documentSerializer, serializerRegistry) },
                { "facets", new BsonDocument(_facets.Select(f => new BsonElement(f.Name, f.Render(documentSerializer, serializerRegistry)))) }
            };
    }

    internal sealed class GeoShapeSearchDefinition<TDocument, TCoordinates> : OperatorSearchDefinition<TDocument>
        where TCoordinates : GeoJsonCoordinates
    {
        private readonly GeoJsonGeometry<TCoordinates> _geometry;
        private readonly GeoShapeRelation _relation;

        public GeoShapeSearchDefinition(
            SearchPathDefinition<TDocument> path,
            GeoShapeRelation relation,
            GeoJsonGeometry<TCoordinates> geometry,
            SearchScoreDefinition<TDocument> score)
                : base(OperatorType.GeoShape, path, score)
        {
            _geometry = Ensure.IsNotNull(geometry, nameof(geometry));
            _relation = relation;
        }

        private protected override BsonDocument RenderArguments(IBsonSerializer<TDocument> documentSerializer, IBsonSerializerRegistry serializerRegistry) =>
            new()
            {
                { "geometry", _geometry.ToBsonDocument() },
                { "relation", _relation.ToCamelCase() }
            };
    }

    internal sealed class GeoWithinSearchDefinition<TDocument, TCoordinates> : OperatorSearchDefinition<TDocument>
        where TCoordinates : GeoJsonCoordinates
    {
        private readonly GeoWithinArea<TCoordinates> _area;

        public GeoWithinSearchDefinition(
            SearchPathDefinition<TDocument> path,
            GeoWithinArea<TCoordinates> area,
            SearchScoreDefinition<TDocument> score)
                : base(OperatorType.GeoWithin, path, score)
        {
            _area = Ensure.IsNotNull(area, nameof(area));
        }

        private protected override BsonDocument RenderArguments(IBsonSerializer<TDocument> documentSerializer, IBsonSerializerRegistry serializerRegistry) =>
            new(_area.Render());
    }

    internal sealed class MoreLikeThisSearchDefinition<TDocument, TLike> : OperatorSearchDefinition<TDocument>
    {
        private readonly TLike[] _like;

        public MoreLikeThisSearchDefinition(IEnumerable<TLike> like)
            : base(OperatorType.MoreLikeThis)
        {
            _like = Ensure.IsNotNull(like, nameof(like)).ToArray();
        }

        private protected override BsonDocument RenderArguments(IBsonSerializer<TDocument> documentSerializer, IBsonSerializerRegistry serializerRegistry)
        {
            var likeSerializer = typeof(TLike) switch
            {
                var t when t == typeof(BsonDocument) => null,
                var t when t == typeof(TDocument) => (IBsonSerializer<TLike>)documentSerializer,
                _ => serializerRegistry.GetSerializer<TLike>()
            };

            return new("like", new BsonArray(_like.Select(document => document.ToBsonDocument(likeSerializer))));
        }
    }

    internal sealed class NearSearchDefinition<TDocument> : OperatorSearchDefinition<TDocument>
    {
        private readonly BsonValue _origin;
        private readonly BsonValue _pivot;

        public NearSearchDefinition(
            SearchPathDefinition<TDocument> path,
            BsonValue origin,
            BsonValue pivot,
            SearchScoreDefinition<TDocument> score = null)
                : base(OperatorType.Near, path, score)
        {
            _origin = origin;
            _pivot = pivot;
        }

        private protected override BsonDocument RenderArguments(IBsonSerializer<TDocument> documentSerializer, IBsonSerializerRegistry serializerRegistry) =>
           new()
           {
                { "origin", _origin },
                { "pivot", _pivot }
           };
    }

    internal sealed class PhraseSearchDefinition<TDocument> : OperatorSearchDefinition<TDocument>
    {
        private readonly SearchQueryDefinition _query;
        private readonly int? _slop;

        public PhraseSearchDefinition(
            SearchPathDefinition<TDocument> path,
            SearchQueryDefinition query,
            int? slop,
            SearchScoreDefinition<TDocument> score)
                : base(OperatorType.Phrase, path, score)
        {
            _query = Ensure.IsNotNull(query, nameof(query));
            _slop = slop;
        }

        private protected override BsonDocument RenderArguments(IBsonSerializer<TDocument> documentSerializer, IBsonSerializerRegistry serializerRegistry) =>
            new()
            {
                { "query", _query.Render() },
                { "slop", _slop, _slop != null }
            };
    }

    internal sealed class QueryStringSearchDefinition<TDocument> : OperatorSearchDefinition<TDocument>
    {
        private readonly FieldDefinition<TDocument> _defaultPath;
        private readonly string _query;

        public QueryStringSearchDefinition(FieldDefinition<TDocument> defaultPath, string query, SearchScoreDefinition<TDocument> score)
            : base(OperatorType.QueryString, score)
        {
            _defaultPath = Ensure.IsNotNull(defaultPath, nameof(defaultPath));
            _query = Ensure.IsNotNull(query, nameof(query));
        }

        private protected override BsonDocument RenderArguments(IBsonSerializer<TDocument> documentSerializer, IBsonSerializerRegistry serializerRegistry) =>
            new()
            {
                { "defaultPath", _defaultPath.Render(documentSerializer, serializerRegistry).FieldName },
                { "query", _query }
            };
    }

    internal sealed class RangeSearchDefinition<TDocument, TField> : OperatorSearchDefinition<TDocument>
        where TField : struct, IComparable<TField>
    {
        private readonly SearchRange<TField> _range;

        public RangeSearchDefinition(
            SearchPathDefinition<TDocument> path,
            SearchRange<TField> range,
            SearchScoreDefinition<TDocument> score)
                : base(OperatorType.Range, path, score)
        {
            _range = range;
        }

        private protected override BsonDocument RenderArguments(IBsonSerializer<TDocument> documentSerializer, IBsonSerializerRegistry serializerRegistry) =>
            new()
            {
                { _range.IsMinInclusive ? "gte" : "gt", () => ToBsonValue(_range.Min.Value), _range.Min != null },
                { _range.IsMaxInclusive ? "lte" : "lt", () => ToBsonValue(_range.Max.Value), _range.Max != null },
            };

        private static BsonValue ToBsonValue(TField value) =>
            value switch
            {
                sbyte v => (BsonInt32)v,
                byte v => (BsonInt32)v,
                short v => (BsonInt32)v,
                ushort v => (BsonInt32)v,
                int v => (BsonInt32)v,
                uint v => (BsonInt32)v,
                long v => (BsonInt64)v,
                float v => (BsonDouble)v,
                double v => (BsonDouble)v,
                DateTime v => (BsonDateTime)v,
                _ => throw new InvalidCastException()
            };
    }

    internal sealed class RegexSearchDefinition<TDocument> : OperatorSearchDefinition<TDocument>
    {
        private readonly bool _allowAnalyzedField;
        private readonly SearchQueryDefinition _query;

        public RegexSearchDefinition(
            SearchPathDefinition<TDocument> path,
            SearchQueryDefinition query,
            bool allowAnalyzedField,
            SearchScoreDefinition<TDocument> score)
                : base(OperatorType.Regex, path, score)
        {
            _query = Ensure.IsNotNull(query, nameof(query));
            _allowAnalyzedField = allowAnalyzedField;
        }

        private protected override BsonDocument RenderArguments(IBsonSerializer<TDocument> documentSerializer, IBsonSerializerRegistry serializerRegistry) =>
            new()
            {
                { "query", _query.Render() },
                { "allowAnalyzedField", _allowAnalyzedField, _allowAnalyzedField },
            };
    }

    internal sealed class SpanSearchDefinition<TDocument> : OperatorSearchDefinition<TDocument>
    {
        private readonly SearchSpanDefinition<TDocument> _clause;

        public SpanSearchDefinition(SearchSpanDefinition<TDocument> clause)
            : base(OperatorType.Span)
        {
            _clause = Ensure.IsNotNull(clause, nameof(clause));
        }

        private protected override BsonDocument RenderArguments(IBsonSerializer<TDocument> documentSerializer, IBsonSerializerRegistry serializerRegistry) =>
            _clause.Render(documentSerializer, serializerRegistry);
    }

    internal sealed class TextSearchDefinition<TDocument> : OperatorSearchDefinition<TDocument>
    {
        private readonly SearchFuzzyOptions _fuzzy;
        private readonly SearchQueryDefinition _query;

        public TextSearchDefinition(
            SearchPathDefinition<TDocument> path,
            SearchQueryDefinition query,
            SearchFuzzyOptions fuzzy,
            SearchScoreDefinition<TDocument> score)
                : base(OperatorType.Text, path, score)
        {
            _query = Ensure.IsNotNull(query, nameof(query));
            _fuzzy = fuzzy;
        }

        private protected override BsonDocument RenderArguments(IBsonSerializer<TDocument> documentSerializer, IBsonSerializerRegistry serializerRegistry) =>
            new()
            {
                { "query", _query.Render() },
                { "fuzzy", () => _fuzzy.Render(), _fuzzy != null },
            };
    }

    internal sealed class WildcardSearchDefinition<TDocument> : OperatorSearchDefinition<TDocument>
    {
        private readonly bool _allowAnalyzedField;
        private readonly SearchQueryDefinition _query;

        public WildcardSearchDefinition(
            SearchPathDefinition<TDocument> path,
            SearchQueryDefinition query,
            bool allowAnalyzedField,
            SearchScoreDefinition<TDocument> score)
                : base(OperatorType.Wildcard, path, score)
        {
            _query = Ensure.IsNotNull(query, nameof(query));
            _allowAnalyzedField = allowAnalyzedField;
        }

        private protected override BsonDocument RenderArguments(IBsonSerializer<TDocument> documentSerializer, IBsonSerializerRegistry serializerRegistry) =>
           new()
           {
                { "query", _query.Render() },
                { "allowAnalyzedField", _allowAnalyzedField, _allowAnalyzedField },
           };
    }
}
