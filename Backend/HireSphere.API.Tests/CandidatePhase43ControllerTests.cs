using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HireSphere.API.Data;
using HireSphere.API.Models;
using HireSphere.API.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HireSphere.API.Tests;

public class CandidatePhase43ControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CandidatePhase43ControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(User User, int ProfileId)> SeedCandidateAsync(string? email = null)
    {
        email ??= $"cand43-{Guid.NewGuid():N}@example.com";
        const string password = "SecurePass123!";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        TestWebApplicationFactory.SeedRoles(db);

        var user = new User
        {
            FullName = "Phase43 Candidate",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = "Candidate",
            Status = UserStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var role = await db.Roles.FirstAsync(r => r.Name == "Candidate");
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });

        var profile = new CandidateProfile
        {
            UserId = user.Id,
            FullName = user.FullName,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.CandidateProfiles.Add(profile);
        await db.SaveChangesAsync();

        return (user, profile.Id);
    }

    private async Task<HttpClient> AuthenticatedClientAsync(string email)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "SecurePass123!"
        });
        login.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = doc.RootElement.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(int AssignmentId, int AssessmentId, int QuestionId, int AppId)> SeedAssignedAssessmentAsync(
        int candidateUserId,
        int maxAttempts = 1,
        DateTime? startsAt = null,
        DateTime? expiresAt = null,
        bool reveal = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var org = new Organization { Name = $"Org-{Guid.NewGuid():N}", CreatedAtUtc = DateTime.UtcNow };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var dept = new Department
        {
            OrganizationId = org.Id,
            Name = "Engineering",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Departments.Add(dept);
        await db.SaveChangesAsync();

        var recruiterEmail = $"rec-{Guid.NewGuid():N}@example.com";
        var recruiter = new User
        {
            FullName = "Recruiter",
            Email = recruiterEmail,
            NormalizedEmail = recruiterEmail.ToUpperInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("SecurePass123!"),
            Role = "Recruiter",
            Status = UserStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Users.Add(recruiter);
        await db.SaveChangesAsync();

        var job = new Job
        {
            Title = "Backend Engineer",
            Description = "Build APIs",
            Location = "Colombo",
            JobType = "Full-time",
            Status = JobStatus.Open,
            RecruiterId = recruiter.Id,
            OrganizationId = org.Id,
            DepartmentId = dept.Id,
            EmploymentType = EmploymentType.FullTime,
            WorkArrangement = WorkArrangement.Hybrid,
            PostedDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var application = new Application
        {
            CandidateId = candidateUserId,
            JobId = job.Id,
            AppliedDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            Status = ApplicationStatus.Assessment,
            CoverLetter = "Hello"
        };
        application.StatusHistory.Add(new ApplicationStatusHistory
        {
            Status = ApplicationStatus.Pending,
            ChangedAtUtc = DateTime.UtcNow.AddDays(-2),
            Notes = "Submitted"
        });
        application.StatusHistory.Add(new ApplicationStatusHistory
        {
            Status = ApplicationStatus.Assessment,
            ChangedAtUtc = DateTime.UtcNow.AddDays(-1),
            Notes = "Assessment assigned"
        });
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var assessment = new SkillAssessment
        {
            JobId = job.Id,
            Title = "C# Skills Check",
            Description = "Basics",
            DurationMinutes = 30,
            MaxAttempts = maxAttempts,
            PassingScorePercent = 50m,
            RevealResultsToCandidate = reveal,
            Status = AssessmentStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };
        assessment.Questions.Add(new AssessmentQuestion
        {
            QuestionText = "2 + 2 = ?",
            QuestionType = "MultipleChoice",
            Points = 10,
            SortOrder = 1,
            OptionsJson = "[\"3\",\"4\",\"5\"]",
            CorrectAnswerKey = "4"
        });
        assessment.Questions.Add(new AssessmentQuestion
        {
            QuestionText = "C# is statically typed.",
            QuestionType = "TrueFalse",
            Points = 5,
            SortOrder = 2,
            OptionsJson = "[\"True\",\"False\"]",
            CorrectAnswerKey = "True"
        });
        db.SkillAssessments.Add(assessment);
        await db.SaveChangesAsync();

        var assignment = new AssessmentAssignment
        {
            SkillAssessmentId = assessment.Id,
            CandidateId = candidateUserId,
            ApplicationId = application.Id,
            AssignedAtUtc = DateTime.UtcNow,
            StartsAtUtc = startsAt,
            ExpiresAtUtc = expiresAt,
            MaxAttempts = maxAttempts,
            RevealResultsToCandidate = reveal,
            Status = AssessmentStatus.Pending
        };
        db.AssessmentAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var questionId = assessment.Questions.OrderBy(q => q.SortOrder).First().Id;
        return (assignment.Id, assessment.Id, questionId, application.Id);
    }

    private async Task<int> SeedInterviewAsync(int candidateUserId, bool requireConfirm = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var org = new Organization { Name = $"OrgI-{Guid.NewGuid():N}", CreatedAtUtc = DateTime.UtcNow };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var dept = new Department
        {
            OrganizationId = org.Id,
            Name = "People",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Departments.Add(dept);
        await db.SaveChangesAsync();

        var recruiterEmail = $"reci-{Guid.NewGuid():N}@example.com";
        var recruiter = new User
        {
            FullName = "Recruiter",
            Email = recruiterEmail,
            NormalizedEmail = recruiterEmail.ToUpperInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("SecurePass123!"),
            Role = "Recruiter",
            Status = UserStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Users.Add(recruiter);
        await db.SaveChangesAsync();

        var job = new Job
        {
            Title = "Interview Role",
            Description = "Desc",
            Location = "Remote",
            JobType = "Full-time",
            Status = JobStatus.Open,
            RecruiterId = recruiter.Id,
            OrganizationId = org.Id,
            DepartmentId = dept.Id,
            EmploymentType = EmploymentType.FullTime,
            WorkArrangement = WorkArrangement.Remote,
            PostedDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var application = new Application
        {
            CandidateId = candidateUserId,
            JobId = job.Id,
            AppliedDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            Status = ApplicationStatus.InterviewScheduled,
            CoverLetter = "Hi"
        };
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var interview = new Interview
        {
            ApplicationId = application.Id,
            InterviewDate = DateTime.UtcNow.AddDays(3),
            TimeZoneId = "Asia/Colombo",
            InterviewType = "Video",
            MeetingLink = "https://meet.example/secret-room",
            MeetingInstructions = "Join 5 minutes early",
            Status = InterviewStatus.Scheduled,
            CandidateResponse = InterviewCandidateResponse.Pending,
            RequireConfirmForMeetingInfo = requireConfirm,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();
        return interview.Id;
    }

    [Fact]
    public async Task AssignedAssessment_IsAccessible_And_Unassigned_IsBlocked()
    {
        var (user, _) = await SeedCandidateAsync();
        var (other, _) = await SeedCandidateAsync();
        var (assignmentId, _, _, _) = await SeedAssignedAssessmentAsync(user.Id);

        var client = await AuthenticatedClientAsync(user.Email);
        var ok = await client.GetAsync($"/api/candidate/assessments/{assignmentId}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var otherClient = await AuthenticatedClientAsync(other.Email);
        var blocked = await otherClient.GetAsync($"/api/candidate/assessments/{assignmentId}");
        Assert.Equal(HttpStatusCode.NotFound, blocked.StatusCode);
    }

    [Fact]
    public async Task Start_Blocked_When_Expired_Or_AttemptLimitReached()
    {
        var (user, _) = await SeedCandidateAsync();
        var (expiredId, _, _, _) = await SeedAssignedAssessmentAsync(
            user.Id,
            expiresAt: DateTime.UtcNow.AddMinutes(-5));

        var client = await AuthenticatedClientAsync(user.Email);
        var expired = await client.PostAsync($"/api/candidate/assessments/{expiredId}/start", null);
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);

        var (limitId, _, questionId, _) = await SeedAssignedAssessmentAsync(user.Id, maxAttempts: 1);
        var start = await client.PostAsync($"/api/candidate/assessments/{limitId}/start", null);
        start.EnsureSuccessStatusCode();
        using var startDoc = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
        var attemptId = startDoc.RootElement.GetProperty("attemptId").GetInt32();

        await client.PutAsJsonAsync($"/api/candidate/assessments/attempts/{attemptId}/answers", new
        {
            answers = new[] { new { questionId, answerValue = "4" } }
        });
        var submit = await client.PostAsync($"/api/candidate/assessments/attempts/{attemptId}/submit", null);
        submit.EnsureSuccessStatusCode();

        var second = await client.PostAsync($"/api/candidate/assessments/{limitId}/start", null);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Submit_CalculatesScore_And_DoesNotExposeAnswerKey()
    {
        var (user, _) = await SeedCandidateAsync();
        var (assignmentId, _, _, _) = await SeedAssignedAssessmentAsync(user.Id, reveal: true);
        var client = await AuthenticatedClientAsync(user.Email);

        var detail = await client.GetAsync($"/api/candidate/assessments/{assignmentId}");
        detail.EnsureSuccessStatusCode();
        var detailJson = await detail.Content.ReadAsStringAsync();
        Assert.DoesNotContain("correctAnswerKey", detailJson, StringComparison.OrdinalIgnoreCase);

        using (var detailDoc = JsonDocument.Parse(detailJson))
        {
            Assert.False(detailDoc.RootElement.GetProperty("questions")[0].TryGetProperty("correctAnswerKey", out _));
            Assert.False(detailDoc.RootElement.GetProperty("questions")[0].TryGetProperty("CorrectAnswerKey", out _));
        }

        var start = await client.PostAsync($"/api/candidate/assessments/{assignmentId}/start", null);
        start.EnsureSuccessStatusCode();
        using var startDoc = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
        var attemptId = startDoc.RootElement.GetProperty("attemptId").GetInt32();
        var q1 = startDoc.RootElement.GetProperty("questions")[0].GetProperty("id").GetInt32();
        var q2 = startDoc.RootElement.GetProperty("questions")[1].GetProperty("id").GetInt32();

        await client.PutAsJsonAsync($"/api/candidate/assessments/attempts/{attemptId}/answers", new
        {
            answers = new[]
            {
                new { questionId = q1, answerValue = "4" },
                new { questionId = q2, answerValue = "True" }
            }
        });

        var submit = await client.PostAsync($"/api/candidate/assessments/attempts/{attemptId}/submit", null);
        submit.EnsureSuccessStatusCode();
        var submitJson = await submit.Content.ReadAsStringAsync();
        Assert.DoesNotContain("correctAnswerKey", submitJson, StringComparison.OrdinalIgnoreCase);

        using var submitDoc = JsonDocument.Parse(submitJson);
        Assert.True(submitDoc.RootElement.GetProperty("resultsVisible").GetBoolean());
        Assert.Equal(15, submitDoc.RootElement.GetProperty("result").GetProperty("score").GetDecimal());
        Assert.Equal(15, submitDoc.RootElement.GetProperty("result").GetProperty("maxScore").GetDecimal());
        Assert.True(submitDoc.RootElement.GetProperty("result").GetProperty("passed").GetBoolean());
    }

    [Fact]
    public async Task Interview_Ownership_Confirm_And_Reschedule_Work()
    {
        var (user, _) = await SeedCandidateAsync();
        var (other, _) = await SeedCandidateAsync();
        var interviewId = await SeedInterviewAsync(user.Id);

        var otherClient = await AuthenticatedClientAsync(other.Email);
        var blocked = await otherClient.GetAsync($"/api/candidate/interviews/{interviewId}");
        Assert.Equal(HttpStatusCode.NotFound, blocked.StatusCode);

        var client = await AuthenticatedClientAsync(user.Email);
        var before = await client.GetFromJsonAsync<JsonElement>($"/api/candidate/interviews/{interviewId}", JsonOptions);
        Assert.False(before.GetProperty("meetingInfoAvailable").GetBoolean());
        Assert.False(before.TryGetProperty("meetingLink", out var linkProp) && linkProp.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(linkProp.GetString()));

        var confirm = await client.PostAsync($"/api/candidate/interviews/{interviewId}/confirm", null);
        confirm.EnsureSuccessStatusCode();
        using var confirmDoc = JsonDocument.Parse(await confirm.Content.ReadAsStringAsync());
        Assert.Equal("Confirmed", confirmDoc.RootElement.GetProperty("candidateResponse").GetString());
        Assert.True(confirmDoc.RootElement.GetProperty("meetingInfoAvailable").GetBoolean());
        Assert.Equal("https://meet.example/secret-room", confirmDoc.RootElement.GetProperty("meetingLink").GetString());

        var interviewId2 = await SeedInterviewAsync(user.Id);
        var reschedule = await client.PostAsJsonAsync(
            $"/api/candidate/interviews/{interviewId2}/reschedule-request",
            new { reason = "Conflict with exam", preferredTimesNote = "Next week mornings" });
        reschedule.EnsureSuccessStatusCode();
        using var rescheduleDoc = JsonDocument.Parse(await reschedule.Content.ReadAsStringAsync());
        Assert.Equal("RescheduleRequested", rescheduleDoc.RootElement.GetProperty("candidateResponse").GetString());
    }

    [Fact]
    public async Task ApplicationTimeline_IsOrdered_And_NotificationCreatedOnSubmit()
    {
        var (user, profileId) = await SeedCandidateAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var org = new Organization { Name = $"OrgN-{Guid.NewGuid():N}", CreatedAtUtc = DateTime.UtcNow };
            db.Organizations.Add(org);
            await db.SaveChangesAsync();
            var dept = new Department { OrganizationId = org.Id, Name = "Eng", CreatedAtUtc = DateTime.UtcNow };
            db.Departments.Add(dept);
            await db.SaveChangesAsync();
            var recruiterEmail = $"recn-{Guid.NewGuid():N}@example.com";
            var recruiter = new User
            {
                FullName = "Recruiter",
                Email = recruiterEmail,
                NormalizedEmail = recruiterEmail.ToUpperInvariant(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("SecurePass123!"),
                Role = "Recruiter",
                Status = UserStatus.Active,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Users.Add(recruiter);
            await db.SaveChangesAsync();
            db.Jobs.Add(new Job
            {
                Title = "Notify Job",
                Description = "D",
                Location = "X",
                JobType = "Full-time",
                Status = JobStatus.Open,
                RecruiterId = recruiter.Id,
                OrganizationId = org.Id,
                DepartmentId = dept.Id,
                EmploymentType = EmploymentType.FullTime,
                WorkArrangement = WorkArrangement.OnSite,
                PostedDate = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            });
            db.Resumes.Add(new Resume
            {
                CandidateProfileId = profileId,
                FilePath = "resumes/t.pdf",
                FileName = "t.pdf",
                IsPrimary = true,
                UploadedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        int jobId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            jobId = await db.Jobs.Where(j => j.Title == "Notify Job").Select(j => j.Id).FirstAsync();
        }

        var client = await AuthenticatedClientAsync(user.Email);
        var submit = await client.PostAsJsonAsync("/api/candidate/applications", new
        {
            jobId,
            termsAccepted = true,
            coverLetter = "Please consider me",
            screeningAnswers = Array.Empty<object>()
        });
        submit.EnsureSuccessStatusCode();
        using var appDoc = JsonDocument.Parse(await submit.Content.ReadAsStringAsync());
        var appId = appDoc.RootElement.GetProperty("id").GetInt32();

        // Seed ordered history and verify ordering
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var app = await db.Applications.Include(a => a.StatusHistory).FirstAsync(a => a.Id == appId);
            app.Status = ApplicationStatus.UnderReview;
            app.StatusHistory.Add(new ApplicationStatusHistory
            {
                Status = ApplicationStatus.UnderReview,
                ChangedAtUtc = DateTime.UtcNow.AddHours(1),
                Notes = "Moved to screening"
            });
            await db.SaveChangesAsync();
        }

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/candidate/applications/{appId}", JsonOptions);
        var history = detail.GetProperty("statusHistory");
        Assert.True(history.GetArrayLength() >= 2);
        var first = history[0].GetProperty("changedAtUtc").GetDateTime();
        var second = history[1].GetProperty("changedAtUtc").GetDateTime();
        Assert.True(first <= second);
        Assert.False(string.IsNullOrWhiteSpace(detail.GetProperty("nextAction").GetString()));

        var notifications = await client.GetFromJsonAsync<JsonElement>("/api/candidate/notifications", JsonOptions);
        Assert.True(notifications.GetProperty("unreadCount").GetInt32() >= 1);
        var items = notifications.GetProperty("items");
        Assert.Contains(items.EnumerateArray(), n =>
            n.GetProperty("category").GetString() == "ApplicationSubmitted");
    }

    [Fact]
    public async Task AssessmentAssignment_Creates_InApp_Notification_WhenSeededWriterUsed()
    {
        var (user, _) = await SeedCandidateAsync();
        var (assignmentId, _, _, _) = await SeedAssignedAssessmentAsync(user.Id);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Notifications.Add(new Notification
            {
                UserId = user.Id,
                Category = "AssessmentAssigned",
                Title = "Assessment assigned",
                Message = "Complete your skills check",
                RelatedEntityType = "AssessmentAssignment",
                RelatedEntityId = assignmentId,
                LinkPath = $"/candidate/assessments/{assignmentId}",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = await AuthenticatedClientAsync(user.Email);
        var list = await client.GetFromJsonAsync<JsonElement>("/api/candidate/notifications", JsonOptions);
        Assert.Contains(list.GetProperty("items").EnumerateArray(), n =>
            n.GetProperty("category").GetString() == "AssessmentAssigned");

        var id = list.GetProperty("items").EnumerateArray()
            .First(n => n.GetProperty("category").GetString() == "AssessmentAssigned")
            .GetProperty("id").GetInt32();

        var read = await client.PostAsync($"/api/candidate/notifications/{id}/read", null);
        read.EnsureSuccessStatusCode();
    }
}
