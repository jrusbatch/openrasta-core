﻿using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using OpenRasta.DI;
using OpenRasta.OperationModel;
using OpenRasta.Pipeline.Contributors;
using OpenRasta.Testing;
using OpenRasta.Testing.Contexts;
using OpenRasta.TypeSystem;
using OpenRasta.Web;
using OpenRasta.Pipeline;
using OpenRasta.Tests.Unit.Fakes;
using Shouldly;

namespace OperationCreationContributor_Specification
{
    public class when_in_the_pipeline : operation_creation_context
    {
        [Test]
        public void it_executes_after_handler_selection()
        {
            given_operation_creator_returns_null();
            given_contributor();

            when_sending_notification<KnownStages.IHandlerSelection>();

            then_contributor_is_executed();
        }
    }

    public class when_there_is_no_handler : operation_creation_context
    {
        [Test]
        public void operations_are_not_created_and_the_processing_continues()
        {
            given_operation_creator_returns(1);
            given_contributor();
            when_sending_notification();
            then_contributor_returns(PipelineContinuation.Continue);
            Context.PipelineData.Operations.ShouldBeEmpty();
        }
    }

    public class when_no_operation_is_created : operation_creation_context
    {
        [Test]
        public void the_operation_result_is_set_to_method_not_allowed()
        {
            given_pipeline_selectedHandler<CustomerHandler>();
            given_operation_creator_returns(0);
            given_contributor();

            when_sending_notification();

            then_contributor_returns(PipelineContinuation.RenderNow);
            Context.OperationResult.LegacyShouldBeOfType<OperationResult.MethodNotAllowed>();
        }
    }

    public class when_operations_are_created : operation_creation_context
    {
        [Test]
        public void an_operation_is_set_and_the_processing_continues()
        {
            given_pipeline_selectedHandler<CustomerHandler>();
            given_operation_creator_returns(1);
            given_contributor();

            when_sending_notification();

            then_contributor_returns(PipelineContinuation.Continue);
          Context.PipelineData.OperationsAsync.LegacyShouldHaveCountOf(1).ShouldBe(Operations);
        }
    }

    public abstract class operation_creation_context : contributor_context<OperationCreatorContributor>
    {
        public List<IOperationAsync> Operations { get; set; }


        protected void given_operation_creator_returns_null()
        {
            given_operation_creator_returns(-1);
        }

        protected void given_operation_creator_returns(int count)
        {
            var mock = new Mock<IOperationCreator>();
            Operations = count >= 0 ? Enumerable.Range(0, count).Select(i => CreateMockOperation()).ToList() : null;
            mock.Setup(x => x.CreateOperations(It.IsAny<IEnumerable<IType>>()))
                .Returns(Operations);
            Resolver.AddDependencyInstance(typeof(IOperationCreator), mock.Object, DependencyLifetime.Singleton);

        }

        IOperationAsync CreateMockOperation()
        {
            var operation = new Mock<IOperationAsync>();
            operation.Setup(x => x.ToString()).Returns("Fake method");
            operation.SetupGet(x => x.Name).Returns("OperationName");
            return operation.Object;
        }

        protected void when_sending_notification()
        {
            when_sending_notification<KnownStages.IHandlerSelection>();
        }
    }
}