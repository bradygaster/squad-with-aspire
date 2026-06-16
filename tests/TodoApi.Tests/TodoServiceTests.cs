using Moq;
using Xunit;

namespace TodoApi.Tests;

public class TodoServiceTests
{
    [Fact(Skip = "Waiting for TodoApi implementation")]
    public void GetAllTodos_ReturnsEmptyList_WhenNoTodos()
    {
        var _ = new Mock<IFormatProvider>();
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public void GetAllTodos_ReturnsOnlyCompleted_WhenFilterIsTrue()
    {
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public void GetAllTodos_ReturnsOnlyActive_WhenFilterIsFalse()
    {
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public void GetTodoById_ReturnsTodo_WhenExists()
    {
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public void GetTodoById_ReturnsNull_WhenNotExists()
    {
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public void CreateTodo_SetsCreatedAtAndId()
    {
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public void CreateTodo_ThrowsOnEmptyTitle()
    {
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public void UpdateTodo_UpdatesFields()
    {
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public void UpdateTodo_ThrowsOnNotFound()
    {
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public void DeleteTodo_RemovesItem()
    {
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public void DeleteTodo_ThrowsOnNotFound()
    {
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public void CompleteTodo_SetsIsCompletedAndCompletedAt()
    {
    }
}
