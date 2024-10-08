﻿using AutoMapper;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Collections;
using System.Net;
using System.Security.Cryptography.Xml;
using YaqraApi.AutoMapperConfigurations;
using YaqraApi.DTOs;
using YaqraApi.DTOs.Author;
using YaqraApi.DTOs.Book;
using YaqraApi.DTOs.Community;
using YaqraApi.DTOs.Genre;
using YaqraApi.Helpers;
using YaqraApi.Models;
using YaqraApi.Models.Enums;
using YaqraApi.Repositories;
using YaqraApi.Repositories.IRepositories;
using YaqraApi.Services.IServices;

namespace YaqraApi.Services
{
    public class CommunityService : ICommunityService
    {
        private readonly ICommunityRepository _communityRepository;
        private readonly IBookService _bookService;
        private readonly IRecommendationService _recommendationService;
        private readonly IBookProxyService _bookProxyService;
        private readonly Mapper _mapper;
        public CommunityService(ICommunityRepository communityRepository, 
            IBookService bookService,
            IRecommendationService recommendationService,
            IBookProxyService bookProxyService)
        {
            _communityRepository = communityRepository;
            _bookService = bookService;
            _recommendationService = recommendationService;
            _bookProxyService = bookProxyService;
            _mapper = AutoMapperConfig.InitializeAutoMapper();
        }
        private async Task<Playlist> AddBooksToPlaylist(Playlist playlist, HashSet<int> booksIds)
        {
            foreach (var book in playlist.Books)
            {
                if (booksIds.Contains(book.Id))
                    booksIds.Remove(book.Id);
            }
            playlist.Books.AddRange((await _bookService.GetRangeAsync(booksIds)).Result);
            return playlist;
        }
        private async Task <DiscussionArticleNews> AddBooksToDiscussion(DiscussionArticleNews discussion, HashSet<int> booksIds)
        {
            if(discussion.Books == null)
                discussion.Books = new List<Book>();

            foreach (var book in discussion.Books)
            {
                if (booksIds.Contains(book.Id))
                    booksIds.Remove(book.Id);
            }
            discussion.Books.AddRange((await _bookService.GetRangeAsync(booksIds)).Result);
            return discussion;
        }
        public async Task<GenericResultDto<PlaylistDto>> AddBooksToPlaylist(int playlistId, HashSet<int> booksIds, string userId)
        {
            var playlist = await _communityRepository.GetPlaylistAsync(playlistId);
            if (playlist == null)
                return new GenericResultDto<PlaylistDto> { Succeeded = false, ErrorMessage = "playlist not found" };

            playlist = await AddBooksToPlaylist(playlist, booksIds);

            foreach (var book in playlist.Books)
            {
                await _bookService.AddTrendingBook(book.Id);
                await _bookService.LoadGenres(book);
                foreach (var genreId in book.Genres.Select(g=>g.Id))
                {
                    await _recommendationService.IncrementPoints(userId, genreId);
                }
                
            }

            await _communityRepository.UpdatePlaylistAsync(playlist);
            return new GenericResultDto<PlaylistDto> { Succeeded = true, Result = (await GetPlaylistAsync(playlistId)).Result };
        }
        public async Task<GenericResultDto<PlaylistDto>> AddPlaylistAsync(AddPlaylistDto playlistDto)
        {
            var playlist = _mapper.Map<Playlist>(playlistDto);
            var original = new List<Book>();

            original.AddRange(playlistDto.BooksIds.Select(id => new Book { Id = id }));

            _bookService.Attach(original);

            playlist.Books = original;
            playlist = await _communityRepository.AddPlaylistAsync(playlist);
            if(playlist == null)
                return new GenericResultDto<PlaylistDto> {Succeeded = false, ErrorMessage = "something went wrong while posting ur playlist" };

            var result = await GetPlaylistAsync(playlist.Id);
            if(result.Succeeded==false)
                return new GenericResultDto<PlaylistDto> { Succeeded = true, ErrorMessage = "ur playlist had been posted successfully but something went wrong while retreiving it" };

            return new GenericResultDto<PlaylistDto> { Succeeded = true, Result = result.Result};
        }
        public async Task<GenericResultDto<ReviewDto>> AddReviewAsync(AddReviewDto review, string userId)
        {
            var original = _mapper.Map<Review>(review);

            if (await _communityRepository.IsBookReviewRepeated(review.UserId, review.BookId))
                return new GenericResultDto<ReviewDto> { Succeeded = false, ErrorMessage = "You already reviewed this book" };
            
            original.Book = null;


            var bookResult = await _bookService.GetByIdAsync(review.BookId);
            if (bookResult.Succeeded == false)
                return new GenericResultDto<ReviewDto> { Succeeded = false, ErrorMessage = "Book Not Found" };
            
            var book = bookResult.Result;
            await _bookService.AddTrendingBook(book.Id);
            foreach (var genreId in book.GenresDto.Select(g => g.GenreId))
            {
                await _recommendationService.IncrementPoints(userId, genreId);
            }

            await _bookProxyService.UpdateRate(review.BookId, review.Rate);

            var result = await _communityRepository.AddReviewAsync(original);
            if (result == null)
                return new GenericResultDto<ReviewDto> { Succeeded = false, ErrorMessage = "something went wrong" };
            
            
            var resultReview = await GetReviewAsync(result.Id);
            return new GenericResultDto<ReviewDto> { Succeeded = true, Result = resultReview.Result };
        }
        public async Task<GenericResultDto<string>> Delete(int postId, string userId)
        {
            var post = await _communityRepository.GetPostAsync(postId);
            if (post == null)
                return new GenericResultDto<string> { Succeeded = false, ErrorMessage = "no post with that id" };
            if (post.UserId != userId)
                return new GenericResultDto<string> { Succeeded = false, ErrorMessage = "this post isn't yours" };
            _communityRepository.Delete(post);

            return new GenericResultDto<string> { Succeeded = true, Result = "post deleted successfully" }; 
        }
        public async Task<GenericResultDto<PlaylistDto>> GetPlaylistAsync(int playlistId)
        {
            var playlist = await _communityRepository.GetPlaylistAsync(playlistId);
            if (playlist == null)
                return new GenericResultDto<PlaylistDto> { Succeeded = false, ErrorMessage = "no playlist with that id was found" };

            var booksDto = new List<BookDto>();
            foreach (var book in playlist.Books)
                booksDto.Add((await _bookService.GetByIdAsync(book.Id)).Result);

            var result = _mapper.Map<PlaylistDto>(playlist);
            result.Books = booksDto;
            return new GenericResultDto<PlaylistDto> { Succeeded = true, Result = result};
        }
        public async Task<GenericResultDto<ReviewDto>> GetReviewAsync(int reviewId)
        {
            var review = await _communityRepository.GetReviewAsync(reviewId);
            if (review == null)
                return new GenericResultDto<ReviewDto> { Succeeded = false, ErrorMessage = "no review with that id was found" };

            var dto = _mapper.Map<ReviewDto>(review);
            dto.Book = _mapper.Map<BookDto>(review.Book);
            foreach (var genre in review.Book.Genres)
                dto.Book.GenresDto.Add(new GenreDto { GenreId = genre.Id, GenreName = genre.Name});
            foreach (var author in review.Book.Authors)
                dto.Book.AuthorsDto.Add(_mapper.Map<AuthorDto>(author));
            return new GenericResultDto<ReviewDto> { Succeeded = true, Result = dto};
        }
        public async Task<GenericResultDto<PlaylistDto>> UpdatePlaylistAsync(UpdatePlaylistDto editedPlaylist, string userId)
        {
            if (editedPlaylist.UserId != userId)
                return new GenericResultDto<PlaylistDto> { Succeeded = false, ErrorMessage = "this playlist isn't yours to update" };

            var existingPlaylist = await _communityRepository.GetPlaylistAsync(editedPlaylist.Id);
            if (existingPlaylist == null)
                return new GenericResultDto<PlaylistDto> { Succeeded = false, ErrorMessage = "Playlist not found" };

            if (existingPlaylist.UserId != userId)
                return new GenericResultDto<PlaylistDto> { Succeeded = false, ErrorMessage = "this playlist isn't yours to update" };

            _mapper.Map(editedPlaylist, existingPlaylist);

            var removeBooksResult = await RemoveBooksFromPlaylist(existingPlaylist.Id, existingPlaylist.Books.Select(b => b.Id).ToHashSet(), existingPlaylist.UserId);
            if(removeBooksResult.Succeeded == true)
            {
                var addBooksResult = await AddBooksToPlaylist(existingPlaylist.Id, editedPlaylist.BooksIds.ToHashSet(), existingPlaylist.UserId);
            }

            var result = await _communityRepository.UpdatePlaylistAsync(existingPlaylist);
            if (result == null)
                return new GenericResultDto<PlaylistDto> { Succeeded = false, ErrorMessage = "something went wrong" };

            var dto = (await GetPlaylistAsync(result.Id)).Result;
            return new GenericResultDto<PlaylistDto> { Succeeded = true, Result = dto };
        }

        public async Task<GenericResultDto<ReviewDto>> UpdateReviewAsync(UpdateReviewDto editedReview, string userId)
        {
            if (editedReview.UserId != userId)
                return new GenericResultDto<ReviewDto> { Succeeded = false, ErrorMessage = "this review isn't yours to update" };

            var existingReview = await _communityRepository.GetReviewAsync(editedReview.Id);
            if (existingReview == null)
                return new GenericResultDto<ReviewDto> { Succeeded = false, ErrorMessage = "Review not found" };

            if (existingReview.UserId != userId)
                return new GenericResultDto<ReviewDto> { Succeeded = false, ErrorMessage = "this review isn't yours to update" };

            _mapper.Map(editedReview, existingReview);

            var result = await _communityRepository.UpdateReviewAsync(existingReview);
            if (result == null)
                return new GenericResultDto<ReviewDto> { Succeeded = false, ErrorMessage = "something went wrong" };

            var dto = (await GetReviewAsync(result.Id)).Result;
            return new GenericResultDto<ReviewDto> { Succeeded = true, Result = dto };
        }
        public async Task<GenericResultDto<PlaylistDto>> RemoveBooksFromPlaylist(int playlistId, HashSet<int> booksIds, string userId)
        {
            var playlist = await _communityRepository.GetPlaylistAsync(playlistId);
            if (playlist == null)
                return new GenericResultDto<PlaylistDto> { Succeeded = false, ErrorMessage = "playlist not found" };
            
            var booksToRemove = playlist.Books.Where(g => booksIds.Contains(g.Id));

            foreach (var book in booksToRemove)
            {
                await _bookService.LoadGenres(book);

                foreach (var genreId in book.Genres.Select(g => g.Id))
                {
                    await _recommendationService.DecrementPoints(userId, genreId);
                }
                
            }

            _bookService.Attach(booksToRemove);

            foreach (var book in booksToRemove)
                playlist.Books.Remove(book);

            await _communityRepository.UpdatePlaylistAsync(playlist);
            var result = await GetPlaylistAsync(playlistId);
            if (result.Succeeded == false)
                return new GenericResultDto<PlaylistDto> { Succeeded = true, ErrorMessage = "playlist updated successfully but something went wrong while retrieving it" };
            return result;
        }
        public async Task<GenericResultDto<DiscussionArticlesNewsDto>> GetDiscussionAsync(int discussionId)
        {
            var discussion = await _communityRepository.GetDiscussionAsync(discussionId);
            if (discussion == null)
                return new GenericResultDto<DiscussionArticlesNewsDto> { Succeeded = false, ErrorMessage = "no discussion with that id was found" };

            var booksDto = new List<BookDto>();
            if(discussion.Books != null)
            {
                foreach (var book in discussion.Books)
                    booksDto.Add((await _bookService.GetByIdAsync(book.Id)).Result);
            }

            var result = _mapper.Map<DiscussionArticlesNewsDto>(discussion);
            result.Books = booksDto;
            return new GenericResultDto<DiscussionArticlesNewsDto> { Succeeded = true, Result = result };
        }
        public async Task<GenericResultDto<DiscussionArticlesNewsDto>> AddDiscussionAsync(AddDiscussionArticleNewsDto discussionDto)
        {
            var discussion = _mapper.Map<DiscussionArticleNews>(discussionDto);
            var original = new List<Book>();
            if(discussionDto.BooksIds != null)
            {
                foreach (var id in discussionDto.BooksIds)
                    original.Add(new Book { Id = id });
                _bookService.Attach(original);
            }
            discussion.Books = original;
            discussion = await _communityRepository.AddDiscussionAsync(discussion);
            if (discussion == null)
                return new GenericResultDto<DiscussionArticlesNewsDto> { Succeeded = false, ErrorMessage = "something went wrong while posting ur discussion" };

            var result = await GetDiscussionAsync(discussion.Id);
            if (result.Succeeded == false)
                return new GenericResultDto<DiscussionArticlesNewsDto> { Succeeded = true, ErrorMessage = "ur discussion had been posted successfully but something went wrong while retreiving it" };

            return new GenericResultDto<DiscussionArticlesNewsDto> { Succeeded = true, Result = result.Result };
        }
        public async Task<GenericResultDto<DiscussionArticlesNewsDto>> UpdateDiscussionAsync(UpdateDiscussionArticleNewsDto editedDiscussion, string userId)
        {
            if (editedDiscussion.UserId != userId)
                return new GenericResultDto<DiscussionArticlesNewsDto> { Succeeded = false, ErrorMessage = "this Topic isn't yours to update" };

            var existingDiscussion = await _communityRepository.GetDiscussionAsync(editedDiscussion.Id);
            if (existingDiscussion == null)
                return new GenericResultDto<DiscussionArticlesNewsDto> { Succeeded = false, ErrorMessage = "Topic not found" };

            if (existingDiscussion.UserId != userId)
                return new GenericResultDto<DiscussionArticlesNewsDto> { Succeeded = false, ErrorMessage = "this Topic isn't yours to update" };

            _mapper.Map(editedDiscussion, existingDiscussion);

            if(editedDiscussion.BooksIds != null)
            {
                var removeBooksResult = await RemoveBooksFromDiscussion(existingDiscussion.Id, existingDiscussion.Books.Select(b => b.Id).ToHashSet(), existingDiscussion.UserId);
                if (removeBooksResult.Succeeded == true)
                {
                    var addBooksResult = await AddBooksToDiscussion(existingDiscussion.Id, editedDiscussion.BooksIds.ToHashSet(), existingDiscussion.UserId);
                }
            }

            var result = await _communityRepository.UpdateDiscussionAsync(existingDiscussion);
            if (result == null)
                return new GenericResultDto<DiscussionArticlesNewsDto> { Succeeded = false, ErrorMessage = "something went wrong" };

            var dto = (await GetDiscussionAsync(result.Id)).Result;
            return new GenericResultDto<DiscussionArticlesNewsDto> { Succeeded = true, Result = dto };
        }
        public async Task<GenericResultDto<DiscussionArticlesNewsDto>> AddBooksToDiscussion(int discussionId, HashSet<int> booksIds, string userId)
        {
            var discussion = await _communityRepository.GetDiscussionAsync(discussionId);
            if (discussion == null)
                return new GenericResultDto<DiscussionArticlesNewsDto> { Succeeded = false, ErrorMessage = "discussion not found" };

            discussion = await AddBooksToDiscussion(discussion, booksIds);

            foreach (var book in discussion.Books)
            {
                await _bookService.AddTrendingBook(book.Id);
                await _bookService.LoadGenres(book);
                foreach (var genreId in book.Genres.Select(g => g.Id))
                {
                    await _recommendationService.IncrementPoints(userId, genreId);
                }
            }

            await _communityRepository.UpdateDiscussionAsync(discussion);
            return new GenericResultDto<DiscussionArticlesNewsDto> { Succeeded = true, Result = (await GetDiscussionAsync(discussionId)).Result };
        }
        public async Task<GenericResultDto<DiscussionArticlesNewsDto>> RemoveBooksFromDiscussion(int discussionId, HashSet<int> booksIds, string userId)
        {
            var discussion = await _communityRepository.GetDiscussionAsync(discussionId);
            if (discussion == null)
                return new GenericResultDto<DiscussionArticlesNewsDto> { Succeeded = false, ErrorMessage = "discussion not found" };
            if(discussion.Books == null)
                return new GenericResultDto<DiscussionArticlesNewsDto> { Succeeded = false, ErrorMessage = "books not found" };

            var booksToRemove = discussion.Books.Where(g => booksIds.Contains(g.Id));

            foreach (var book in booksToRemove)
            {
                await _bookService.LoadGenres(book);

                foreach (var genreId in book.Genres.Select(g => g.Id))
                    {
                        await _recommendationService.DecrementPoints(userId, genreId);
                    }
            }

            _bookService.Attach(booksToRemove);

            foreach (var book in booksToRemove)
                discussion.Books.Remove(book);

            await _communityRepository.UpdateDiscussionAsync(discussion);
            var result = await GetDiscussionAsync(discussionId);
            if (result.Succeeded == false)
                return new GenericResultDto<DiscussionArticlesNewsDto> { Succeeded = true, ErrorMessage = "discussion updated successfully but something went wrong while retrieving it" };
            return result;
        }
        public async Task<GenericResultDto<PagedResult<ReviewDto>>> GetAllReviewsAsync(int page)
        {
            page = page == 0 ? 1 : page;
            var reviews = await _communityRepository.GetAllReviewsAsync(page);
            var reviewsDto = new List<ReviewDto>();
            foreach (var review in reviews)
                reviewsDto.Add(_mapper.Map<ReviewDto>(review));

            var result = new PagedResult<ReviewDto>
            {
                PageSize = Pagination.Posts,
                Data = reviewsDto,
                PageNumber = page,
                TotalPages = Pagination.CalculatePagesCount(_communityRepository.GetAllReviewsCount(), Pagination.Posts)
            };

            return new GenericResultDto<PagedResult<ReviewDto>> { Succeeded = true, Result = result };
        }
        public async Task<GenericResultDto<PagedResult<PlaylistDto>>> GetAllPlaylistsAsync(int page)
        {
            page = page == 0 ? 1 : page;
            var playlists = await _communityRepository.GetAllPlaylistsAsync(page);
            var playlistsDto = new List<PlaylistDto>();
            foreach (var playlist in playlists)
                playlistsDto.Add(_mapper.Map<PlaylistDto>(playlist));

            var result = new PagedResult<PlaylistDto>
            {
                PageSize = Pagination.Posts,
                Data = playlistsDto,
                PageNumber = page,
                TotalPages = Pagination.CalculatePagesCount(_communityRepository.GetAllPlaylistsCount(), Pagination.Posts)
            };

            return new GenericResultDto<PagedResult<PlaylistDto>> { Succeeded = true, Result = result };

        }
        public async Task<GenericResultDto<PagedResult<DiscussionArticlesNewsDto>>> GetAllDiscussionsAsync(int page, DiscussionArticleNewsTag tag)
        {
            page = page == 0 ? 1 : page;
            var discussions = await _communityRepository.GetAllDiscussionsAsync(page, tag);
            var discussionsDto = new List<DiscussionArticlesNewsDto>();
            foreach (var discussion in discussions)
                discussionsDto.Add(_mapper.Map<DiscussionArticlesNewsDto>(discussion));

            var result = new PagedResult<DiscussionArticlesNewsDto>
            {
                PageSize = Pagination.Posts,
                Data = discussionsDto,
                PageNumber = page,
                TotalPages = Pagination.CalculatePagesCount(_communityRepository.GetAllDiscussionsCount(tag), Pagination.Posts)
            };

            return new GenericResultDto<PagedResult<DiscussionArticlesNewsDto>> { Succeeded = true, Result = result };

        }
        public async Task<GenericResultDto<LikeDto>> LikeAsync(int postId, string userId)
        {
            var post = await _communityRepository.GetPostAsync(postId);
            if (post == null)
                return null;

            var result = new GenericResultDto<LikeDto> { Succeeded = true, Result = new LikeDto()};

            var postLike = post.PostLikes.SingleOrDefault(p => p.UserId == userId);
            if (postLike != null)//unlike
            {
                post.PostLikes.Remove(postLike);
                post.LikeCount--;
                result.Result.IsLiked = false;//unlike
            }
            else//like
            {
                post.PostLikes.Add(new PostLikes { PostId = postId, UserId = userId });
                post.LikeCount++;
                result.Result.IsLiked = true;//like
            }

            result.Result.LikesCount = post.LikeCount;

            _communityRepository.UpdatePost(post);
            return result;
        }
        public async Task<GenericResultDto<PagedResult<ReviewDto>>> GetUserReviews(string userId, int page)
        {
            page = page == 0 ? 1 : page;
            var reviews = await _communityRepository.GetUserReviews(userId, page);
            if (reviews == null)
                return new GenericResultDto<PagedResult<ReviewDto>> { Succeeded = false, ErrorMessage = "user not found" };

            var reviewsDto = new List<ReviewDto>();
            foreach (var review in reviews)
                reviewsDto.Add(_mapper.Map<ReviewDto>(review));

            var result = new PagedResult<ReviewDto>
            {
                PageSize = Pagination.Posts,
                Data = reviewsDto,
                PageNumber = page,
                TotalPages = Pagination.CalculatePagesCount(_communityRepository.GetUserReviewsCount(userId), Pagination.Posts)
            };

            return new GenericResultDto<PagedResult<ReviewDto>> { Succeeded = true, Result = result };

        }
        public async Task<GenericResultDto<PagedResult<PlaylistDto>>> GetUserPlaylists(string userId, int page)
        {
            page = page == 0 ? 1 : page;
            var playlists = await _communityRepository.GetUserPlaylists(userId, page);
            if (playlists == null)
                return new GenericResultDto<PagedResult<PlaylistDto>> { Succeeded = false, ErrorMessage = "user not found" };

            var playlistsDto = new List<PlaylistDto>();
            foreach (var playlist in playlists)
                playlistsDto.Add(_mapper.Map<PlaylistDto>(playlist));

            var result = new PagedResult<PlaylistDto>
            {
                PageSize = Pagination.Posts,
                Data = playlistsDto,
                PageNumber = page,
                TotalPages = Pagination.CalculatePagesCount(_communityRepository.GetUserPlaylistsCount(userId), Pagination.Posts)
            };

            return new GenericResultDto<PagedResult<PlaylistDto>> { Succeeded = true, Result = result };
        }
        public async Task<GenericResultDto<PagedResult<DiscussionArticlesNewsDto>>> GetUserDiscussions(string userId, int page)
        {
            page = page == 0 ? 1 : page;
            var discussions = await _communityRepository.GetUserDiscussions(userId, page);
            if (discussions == null)
                return new GenericResultDto<PagedResult<DiscussionArticlesNewsDto>> { Succeeded = false, ErrorMessage = "user not found" };

            var discussionsDto = new List<DiscussionArticlesNewsDto>();
            foreach (var dis in discussions)
                discussionsDto.Add(_mapper.Map<DiscussionArticlesNewsDto>(dis));

            var result = new PagedResult<DiscussionArticlesNewsDto>
            {
                PageSize = Pagination.Posts,
                Data = discussionsDto,
                PageNumber = page,
                TotalPages = Pagination.CalculatePagesCount(_communityRepository.GetUserDiscussionsCount(userId), Pagination.Posts)
            };

            return new GenericResultDto<PagedResult<DiscussionArticlesNewsDto>> { Succeeded = true, Result = result };
        }
        public async Task<GenericResultDto<CommentDto>> AddCommentAsync(CommentDto dto)
        {
            var comment = _mapper.Map<Comment>(dto);
            comment = await _communityRepository.AddCommentAsync(comment);
            if (comment == null)
                return new GenericResultDto<CommentDto> { Succeeded = false, ErrorMessage = "something went wrong while posting ur comment" };

            var result = (await GetCommentAsync(comment.Id)).Result;
            return new GenericResultDto<CommentDto> { Succeeded = true, Result = result };
        }
        public async Task<GenericResultDto<CommentDto>> GetCommentAsync(int commentId)
        {
            var comment = await _communityRepository.GetCommentAsync(commentId);
            if (comment == null)
                return new GenericResultDto<CommentDto> { Succeeded = false, ErrorMessage = "comment not found" };

            return new GenericResultDto<CommentDto> { Succeeded = true, Result = _mapper.Map<CommentDto>(comment) };
        }
        public async Task<GenericResultDto<string>> DeleteCommentAsync(int commentId, string userId)
        {
            var comment = await _communityRepository.GetCommentAsync(commentId);
            if (comment == null)
                return new GenericResultDto<string> { Succeeded = false, ErrorMessage = "comment not found" };
            if (comment.UserId != userId)
                return new GenericResultDto<string> { Succeeded = false, ErrorMessage = "this comment isn't yours to delete" };
            _communityRepository.DeleteComment(comment);
            return new GenericResultDto<string> { Succeeded = true, Result = "comment deleted successfully" };
        }
        public async Task<GenericResultDto<PagedResult<CommentDto>>> GetPostCommentsAsync(int postId, int page)
        {
            page = page == 0 ? 1 : page;
            var comments = await _communityRepository.GetPostCommentsAsync(postId, page);
            if (comments == null)
                return new GenericResultDto<PagedResult<CommentDto>> { Succeeded = false, ErrorMessage = "post not found" };
            var commentsDto = new List<CommentDto>();
            foreach (var comment in comments)
                commentsDto.Add(_mapper.Map<CommentDto>(comment));

            var result = new PagedResult<CommentDto>
            {
                PageSize = Pagination.Comments,
                Data = commentsDto,
                PageNumber = page,
                TotalPages = Pagination.CalculatePagesCount(await _communityRepository.GetPostCommentsCount(postId), Pagination.Comments),
            };

            return new GenericResultDto<PagedResult<CommentDto>> { Succeeded = true, Result = result };
        }
        public async Task<GenericResultDto<LikeDto>> LikeCommentsAsync(int commentId, string userId)
        {
            var comment = await _communityRepository.GetCommentAsync(commentId);

            if (comment == null)
                return new GenericResultDto<LikeDto> { Succeeded = false, ErrorMessage = "comment not found" };

            var result = new GenericResultDto<LikeDto> { Succeeded = true, Result = new LikeDto() };

            var commentLike = comment.CommentLikes.SingleOrDefault(p => p.UserId == userId);
            if (commentLike != null)
            {
                comment.CommentLikes.Remove(commentLike);
                comment.LikeCount--;
                result.Result.IsLiked = false;//unlike
            }
            else
            {
                comment.CommentLikes.Add(new CommentLikes { CommentId = commentId, UserId = userId });
                comment.LikeCount++;
                result.Result.IsLiked = true;//like
            }

            result.Result.LikesCount = comment.LikeCount;

            _communityRepository.UpdateComment(comment);
            return result;

        }
        public async Task<GenericResultDto<CommentDto>> UpdateCommentAsync(int commentId, string content, string userId)
        {
            var comment = await _communityRepository.GetCommentAsync(commentId);
            if (comment == null)
                return new GenericResultDto<CommentDto> { Succeeded = false, ErrorMessage = "comment not found" };
            if (comment.UserId != userId)
                return new GenericResultDto<CommentDto> { Succeeded = false, ErrorMessage = "this comment isn't yours to update" };
            comment.Content = content;
            comment = _communityRepository.UpdateComment(comment);
            return new GenericResultDto<CommentDto> { Succeeded = true, Result = _mapper.Map<CommentDto>(comment) };
        }
        public async Task<GenericResultDto<ArrayList>> GetFollowingsPostsAsync(IEnumerable<string> followingsIds, int page)
        {
            var posts = await _communityRepository.GetFollowingsPostsAsync(followingsIds, page);
            if (posts.IsNullOrEmpty())
                return new GenericResultDto<ArrayList> { Succeeded = true, Result = new ArrayList() };
            return new GenericResultDto<ArrayList> { Succeeded = true, Result = ConvertPostParentToChildren(posts) };
        }
        private ArrayList ConvertPostParentToChildren(List<Post> posts)
        {
            var arr = new ArrayList();
            foreach (var post in posts)
            {
                if(post is Review)
                {
                    var review = post as Review;
                    var reviewDto = _mapper.Map<ReviewDto>(review);
                    reviewDto.Book = _mapper.Map<BookDto>(review.Book);
                    arr.Add(reviewDto);
                }
                else if (post is Playlist)
                {
                    var playlistDto = _mapper.Map<PlaylistDto>(post as Playlist);
                    arr.Add(playlistDto);
                }
                else // discussionArticleNews
                {
                    var discussionDto = _mapper.Map<DiscussionArticlesNewsDto>(post as DiscussionArticleNews);
                    arr.Add(discussionDto);
                }
            }
            return arr;
        }
        public async Task<GenericResultDto<ArrayList>> GetPostsAsync(int page)
        {
            var posts = await _communityRepository.GetPostsAsync(page);
            if (posts.IsNullOrEmpty())
                return new GenericResultDto<ArrayList> { Succeeded = true, Result = new ArrayList() };
            return new GenericResultDto<ArrayList> { Succeeded = true, Result = ConvertPostParentToChildren(posts) };
        }
        public async Task<GenericResultDto<string>> GetPostUserIdAsync(int postId)
        {
            var userId = await _communityRepository.GetPostUserIdAsync(postId);
            if (userId == null)
                return new GenericResultDto<string> { Succeeded = false };
            return new GenericResultDto<string> { Succeeded = true, Result = userId };
        }
        public async Task<GenericResultDto<string>> GetCommentUserIdAsync(int commentId)
        {
            var userId = await _communityRepository.GetCommentUserIdAsync(commentId);
            if (userId == null)
                return new GenericResultDto<string> { Succeeded = false };
            return new GenericResultDto<string> { Succeeded = true, Result = userId };
        }

        public async Task<GenericResultDto<Post>> GetPostAsync(int postId)
        {
            var post = await _communityRepository.GetPostAsync(postId);
            return new GenericResultDto<Post> { Succeeded = true, Result = post };
        }

        public async Task<bool> IsPostLikedAsync(int postId, string userId)
        {
            if (userId == null) return false;
            return (await _communityRepository.IsPostLiked(postId, userId));
        }
        public async Task<HashSet<int>> ArePostsLiked(List<int> postsIds, string userId)
        {
            var result = new HashSet<int>();
            if (userId == null)
                return result;

            return await _communityRepository.ArePostsLiked(new HashSet<int>(postsIds.Distinct()), userId);
        }

        public async Task<bool> IsCommentLikedAsync(int commentId, string userId)
        {
            if (userId == null) return false;
            return (await _communityRepository.IsCommentLiked(commentId, userId));
        }

        public async Task<HashSet<int>> AreCommentsLiked(List<int> commentsIds, string userId)
        {
            var result = new HashSet<int>();
            if (userId == null)
                return result;

            return await _communityRepository.AreCommentsLiked(new HashSet<int>(commentsIds.Distinct()), userId);
        }

    }
}
