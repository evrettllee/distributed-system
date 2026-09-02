using Server.Handlers;
using Server.Models;
using Server.Network;
using Server.Repositories;
using Server.Services;

var students = new JsonRepository<Student>("Data/students.json");
var teachers = new JsonRepository<Teacher>("Data/teachers.json");
var courses = new JsonRepository<Course>("Data/courses.json");
var lessons = new JsonRepository<Lesson>("Data/lessons.json");
var assignments = new JsonRepository<Assignment>("Data/assignments.json");
var enrollments = new JsonRepository<Enrollment>("Data/enrollments.json");

var service = new EducationService(students, courses, enrollments, assignments);
var handler = new OperationHandler(service);
var server = new TcpServer(handler);

server.Start();